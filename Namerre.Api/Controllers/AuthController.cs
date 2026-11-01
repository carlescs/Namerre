using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Namerre.Application.Authentication;
using Namerre.Application.Renaming;
using Namerre.Authentication;
using Namerre.Infrastructure.Diagnostics;

namespace Namerre.Controllers;

/// <summary>Sign-in, first-run setup and the signed-in user's own account.</summary>
[ApiController]
[Route("api/auth")]
[TypeFilter(typeof(AccountExceptionFilter))]
public sealed class AuthController(UserAccounts accounts, OidcLogin oidc,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions, IAntiforgery antiforgery, ILogger<AuthController> logger,
    IDataProtectionProvider? dataProtection = null, RenameLimits? limits = null, TimeProvider? time = null)
    : ControllerBase
{
    private static readonly TimeSpan LinkTicketLifetime = TimeSpan.FromMinutes(5);
    private DateTimeOffset Now => (time ?? TimeProvider.System).GetUtcNow();

    [AllowAnonymous]
    [HttpGet("session")]
    public IActionResult Session()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            Secure = true,
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        var user = CurrentUser();
        return Ok(new
        {
            username = user?.Username,
            userId = user?.Id,
            role = user?.Role.ToString(),
            requiresSetup = accounts.RequiresSetup,
            oidc = oidc.Enabled ? new { displayName = oidc.DisplayName } : null,
            // The configured administrator is changed in server configuration only.
            managedByConfiguration = user?.Id == UserAccounts.ConfiguredUserId,
            canChangePassword = user is { HasPassword: true, Id: not UserAccounts.ConfiguredUserId },
            hasPassword = user?.HasPassword ?? false,
            singleSignOnSession = user is not null && UserSession.IsSingleSignOn(User),
            signedInAt = user is null ? null : UserSession.SignedInAt(User),
            recentSignIn = user is not null && UserSession.IsRecent(User, Now),
            logins = user?.Logins.Select(LoginView.From).ToArray(),
            // Configured workload limits, so the app shows the numbers the server enforces.
            limits = user is null ? null : limits ?? RenameLimits.Default
        });
    }

    /// <param name="returnTo"><c>account</c> confirms a sensitive change: the provider must authenticate the user
    /// again (<c>prompt=login</c>, <c>max_age=0</c>) and the flow returns to the account page. Anything else is a
    /// normal sign-in that returns to the app. Fixed addresses avoid open redirects.</param>
    [AllowAnonymous]
    [HttpGet("oidc/login")]
    public Task<IActionResult> OidcLogin(CancellationToken cancellationToken, string? returnTo = null)
        => Challenge(returnTo == "account" ? FreshSignIn("/account") : new AuthenticationProperties { RedirectUri = "/" },
            "/?signInError=unavailable", cancellationToken);

    /// <summary>A challenge that makes the provider authenticate again and report when (<c>auth_time</c>).</summary>
    private static OpenIdConnectChallengeProperties FreshSignIn(string redirectUri) => new()
    {
        RedirectUri = redirectUri,
        Prompt = OpenIdConnectPrompt.Login,
        MaxAge = TimeSpan.Zero
    };

    /// <summary>
    /// Starts linking a single sign-on identity to the signed-in user. Linking is sensitive, so it needs a recent
    /// sign-in or the user's password. Returns a short-lived address for the browser to open.
    /// </summary>
    [EnableRateLimiting(AuthenticationSetup.LoginPolicy)]
    [HttpPost("oidc/link")]
    public IActionResult LinkSingleSignOn(ConfirmRequest request)
    {
        if (!oidc.Enabled)
            return NotFound();
        var user = RequireUser();
        EnsureEditable(user);
        EnsureConfirmed(user, request.Password);
        var ticket = Protector().ToTimeLimitedDataProtector().Protect(
            string.Join('|', user.Id.ToString(CultureInfo.InvariantCulture), user.Stamp), LinkTicketLifetime);
        return Ok(new { url = "/api/auth/oidc/link?ticket=" + Uri.EscapeDataString(ticket) });
    }

    [HttpGet("oidc/link")]
    public async Task<IActionResult> LinkSingleSignOnChallenge(string? ticket, CancellationToken cancellationToken)
    {
        if (!oidc.Enabled)
            return NotFound();
        var user = CurrentUser();
        string[] parts;
        try
        {
            parts = Protector().ToTimeLimitedDataProtector().Unprotect(ticket ?? "").Split('|');
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or
            FormatException)
        {
            return Redirect("/account?linkError=expired");
        }
        // The ticket only works for the user, and the user version, that asked for it.
        if (user is null || parts.Length != 2 || parts[0] != user.Id.ToString(CultureInfo.InvariantCulture) ||
            parts[1] != user.Stamp)
            return Redirect("/account?linkError=expired");
        // The challenge properties are protected, so the callback can trust which user to link to.
        // Ask the provider to sign in again rather than silently reusing whatever session the browser holds.
        var properties = FreshSignIn("/account");
        properties.Items[Authentication.OidcLogin.LinkUserItem] = parts[0];
        properties.Items[Authentication.OidcLogin.LinkStampItem] = parts[1];
        return await Challenge(properties, "/account?linkError=unavailable", cancellationToken);
    }

    [HttpGet("logins")]
    public IActionResult Logins() => Ok(RequireUser().Logins.Select(LoginView.From));

    [HttpDelete("logins/{loginId:long}")]
    public async Task<IActionResult> Unlink(long loginId)
    {
        var user = RequireUser();
        EnsureEditable(user);
        var updated = accounts.Unlink(Reference(user), user.Id, loginId);
        await RenewAsync(updated);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.LoginPolicy)]
    [HttpPost("setup")]
    public IActionResult Setup(SetupRequest request)
    {
        if (!UserAccounts.IsValidUsername(request.Username))
            return BadRequest(new { message = "Enter an administrator username." });
        try
        {
            var result = accounts.CompleteSetup(request.Token, request.Username, request.Password);
            if (result == SetupResult.AlreadyConfigured)
                return Conflict(new { message = "The administrator is already configured. Sign in instead." });
            if (result == SetupResult.InvalidToken)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Invalid setup token." });
            if (result == SetupResult.WeakPassword)
                return BadRequest(new { message = Passwords.WeakPasswordMessage });
            logger.LogInformation("Administrator installation completed.");
            return NoContent();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            RenameException or UserStorageException)
        {
            logger.LogError(exception, "Unable to save administrator configuration.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Unable to save the administrator. Check the database and server permissions, then retry." });
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.LoginPolicy)]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var verified = accounts.Authenticate(request.Username, request.Password);
        if (verified is null)
        {
            // The attempted username is not logged: people sometimes type a password into it.
            NamerreTelemetry.SignIns.Add(1, new("namerre.auth.method", "password"), new("namerre.auth.outcome", "failure"));
            logger.LogWarning("Failed sign-in from {RemoteAddress}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { message = "Invalid username or password." });
        }

        await SignInAsync(UserSession.Create(verified, UserSession.PasswordMethod, null, Now));
        accounts.RecordSignIn(verified);
        NamerreTelemetry.SignIns.Add(1, new("namerre.auth.method", "password"), new("namerre.auth.outcome", "success"));
        logger.LogInformation("User {UserId} ({Username}) signed in from {RemoteAddress}", verified.Id,
            LogText.Clean(verified.Username), HttpContext.Connection.RemoteIpAddress);
        return NoContent();
    }

    /// <summary>Confirms the password again, so sensitive changes in the next few minutes need no further prompt.</summary>
    [EnableRateLimiting(AuthenticationSetup.LoginPolicy)]
    [HttpPost("reauthenticate")]
    public async Task<IActionResult> Reauthenticate(ConfirmRequest request)
    {
        var user = RequireUser();
        if (!accounts.VerifyPassword(user, request.Password))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "InvalidPassword", message = "The password is incorrect." });
        await RenewAsync(user, Now);
        return NoContent();
    }

    /// <summary>
    /// Changes the password, or sets one for a user who only signed in with single sign-on so far. Setting a first
    /// password needs a recent sign-in instead of the current password.
    /// </summary>
    [EnableRateLimiting(AuthenticationSetup.LoginPolicy)]
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = RequireUser();
        EnsureEditable(user);
        var updated = accounts.SetPassword(Reference(user), user.Id, request.CurrentPassword, request.NewPassword,
            UserSession.IsRecent(User, Now));
        // The credential stamp changed, which signs out every other session; keep this one.
        await RenewAsync(updated);
        return NoContent();
    }

    [HttpDelete("password")]
    public async Task<IActionResult> RemovePassword()
    {
        var user = RequireUser();
        EnsureEditable(user);
        EnsureConfirmed(user, null);
        var updated = accounts.RemovePassword(Reference(user), user.Id);
        await RenewAsync(updated);
        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var endSessionUrl = UserSession.IsSingleSignOn(User) && oidc.Enabled
            ? await EndSessionUrlAsync(await HttpContext.GetTokenAsync(Authentication.OidcLogin.IdTokenName),
                cancellationToken)
            : null;
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // The browser must navigate to the provider itself, so the address is returned instead of a redirect.
        return endSessionUrl is null ? NoContent() : Ok(new { endSessionUrl });
    }

    private async Task<IActionResult> Challenge(AuthenticationProperties properties, string unavailable,
        CancellationToken cancellationToken)
    {
        if (!oidc.Enabled)
            return NotFound();
        try
        {
            // Load the provider metadata first so an unreachable identity provider is not a bare server error.
            await oidcOptions.Get(Authentication.OidcLogin.Scheme).ConfigurationManager!
                .GetConfigurationAsync(cancellationToken);
            // The challenge itself may call the provider (pushed authorization requests), so it is guarded too.
            await HttpContext.ChallengeAsync(Authentication.OidcLogin.Scheme, properties);
            return new EmptyResult();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
            HttpRequestException or OpenIdConnectProtocolException)
        {
            logger.LogWarning(exception, "The identity provider is unavailable.");
            if (!Response.HasStarted)
                Response.Clear();
            return Redirect(unavailable);
        }
    }

    /// <summary>Builds the identity provider's RP-initiated logout address, or null when it has none.</summary>
    private async Task<string?> EndSessionUrlAsync(string? idToken, CancellationToken cancellationToken)
    {
        var options = oidcOptions.Get(Authentication.OidcLogin.Scheme);
        try
        {
            var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
            if (string.IsNullOrEmpty(configuration.EndSessionEndpoint))
                return null;
            return new OpenIdConnectMessage
            {
                IssuerAddress = configuration.EndSessionEndpoint,
                IdTokenHint = idToken,
                // Lets the provider identify the client for sessions started before ID tokens were kept.
                ClientId = options.ClientId,
                // A fixed return address avoids open redirects.
                PostLogoutRedirectUri = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/"
            }.CreateLogoutRequestUrl();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
            HttpRequestException or OpenIdConnectProtocolException)
        {
            // Still end the Namerre session; the provider session simply outlives it.
            logger.LogWarning(exception, "The identity provider is unavailable; signing out of Namerre only.");
            return null;
        }
    }

    private UserRecord? CurrentUser()
        => User.Identity?.IsAuthenticated == true && UserSession.UserId(User) is { } id ? accounts.Find(id) : null;

    private UserRecord RequireUser() => CurrentUser() ?? throw AccountException.NotFound();

    private static UserReference Reference(UserRecord user) => new(user.Id, user.Username);

    private static void EnsureEditable(UserRecord user)
    {
        if (user.Id == UserAccounts.ConfiguredUserId)
            throw AccountException.Configured();
    }

    /// <summary>Requires a recent sign-in, or the user's password when they have one.</summary>
    private void EnsureConfirmed(UserRecord user, string? password)
    {
        if (!UserSession.IsRecent(User, Now) && !accounts.VerifyPassword(user, password))
            throw AccountException.RecentSignIn();
    }

    private IDataProtector Protector()
        => (dataProtection ?? HttpContext.RequestServices.GetRequiredService<IDataProtectionProvider>())
            .CreateProtector("Namerre.Authentication.LinkSingleSignOn");

    /// <summary>Re-issues this session for the changed user, keeping its method, provider tokens and expiry.</summary>
    private async Task RenewAsync(UserRecord user, DateTimeOffset? signedInAt = null)
    {
        var current = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = current?.Properties ?? new AuthenticationProperties();
        properties.IsPersistent = false;
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            UserSession.Renew(User, user, signedInAt), properties);
    }

    private Task SignInAsync(System.Security.Claims.ClaimsPrincipal principal)
        => HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });
}

public sealed record LoginView(long Id, string Issuer, string DisplayName, DateTimeOffset LinkedAt,
    DateTimeOffset? LastUsedAt)
{
    public static LoginView From(UserLoginRecord login)
        => new(login.Id, login.Issuer, login.DisplayName, login.LinkedAt, login.LastUsedAt);
}

public sealed record LoginRequest(
    [Required, StringLength(128)] string Username,
    [Required, StringLength(1024)] string Password);

public sealed record SetupRequest(
    [Required, StringLength(64, MinimumLength = 64)] string Token,
    [Required, StringLength(128)] string Username,
    [Required, StringLength(1024)] string Password);

/// <param name="CurrentPassword">Required when the user already has a password.</param>
public sealed record ChangePasswordRequest(
    [StringLength(1024)] string? CurrentPassword,
    [Required, StringLength(1024)] string NewPassword);

/// <param name="Password">The user's password, when they have one and did not sign in recently.</param>
public sealed record ConfirmRequest([StringLength(1024)] string? Password);
