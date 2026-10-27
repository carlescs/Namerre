using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Namerre.Authentication;
using Namerre.Application.Authentication;
using Namerre.Application.Renaming;

namespace Namerre.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AdminAccount account, OidcLogin oidc,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions, IAntiforgery antiforgery, ILogger<AuthController> logger) : ControllerBase
{
    private static readonly string WeakPasswordMessage =
        $"Use a password of at least {AdminAccount.MinimumPasswordLength} characters.";

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
        return Ok(new
        {
            username = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null,
            requiresSetup = account.RequiresSetup,
            oidc = oidc.Enabled ? new { displayName = oidc.DisplayName } : null,
            // A single sign-on session does not know the local administrator password.
            canChangePassword = account.CanChangePassword && !IsSingleSignOnSession
        });
    }

    [AllowAnonymous]
    [HttpGet("oidc/login")]
    public async Task<IActionResult> OidcLogin(CancellationToken cancellationToken)
    {
        if (!oidc.Enabled)
            return NotFound();
        try
        {
            // Load the provider metadata first so an unreachable identity provider is not a bare server error.
            await oidcOptions.Get(Authentication.OidcLogin.Scheme).ConfigurationManager!
                .GetConfigurationAsync(cancellationToken);
            // The challenge itself may call the provider (pushed authorization requests), so it is guarded too.
            // A fixed return address avoids open redirects.
            await HttpContext.ChallengeAsync(Authentication.OidcLogin.Scheme,
                new AuthenticationProperties { RedirectUri = "/" });
            return new EmptyResult();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
            HttpRequestException or OpenIdConnectProtocolException)
        {
            logger.LogWarning(exception, "The identity provider is unavailable.");
            if (!Response.HasStarted)
                Response.Clear();
            return Redirect("/?signInError=unavailable");
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.LoginPolicy)]
    [HttpPost("setup")]
    public IActionResult Setup(SetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new { message = "Enter an administrator username." });
        try
        {
            var result = account.CompleteSetup(request.Token, request.Username, request.Password);
            if (result == SetupResult.AlreadyConfigured)
                return Conflict(new { message = "The administrator is already configured. Sign in instead." });
            if (result == SetupResult.InvalidToken)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Invalid setup token." });
            if (result == SetupResult.WeakPassword)
                return BadRequest(new { message = WeakPasswordMessage });
            logger.LogInformation("Administrator installation completed.");
            return NoContent();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            RenameException or AdministratorStorageException)
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
        var verified = account.Authenticate(request.Username, request.Password);
        if (verified is null)
            return Unauthorized(new { message = "Invalid username or password." });

        await SignInAsync(verified);
        return NoContent();
    }

    [EnableRateLimiting(AuthenticationSetup.LoginPolicy)]
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        if (IsSingleSignOnSession)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Sign in with the local administrator account to change its password." });
        PasswordChangeResult result;
        AdminCredentials? changed;
        try
        {
            result = account.ChangePassword(request.CurrentPassword, request.NewPassword, out changed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            RenameException or AdministratorStorageException)
        {
            logger.LogError(exception, "Unable to save the administrator password.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Unable to save the password. Check the database and server permissions, then retry." });
        }

        if (result == PasswordChangeResult.InvalidPassword)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "The current password is incorrect." });
        if (result == PasswordChangeResult.WeakPassword)
            return BadRequest(new { message = WeakPasswordMessage });
        if (result == PasswordChangeResult.ManagedByConfiguration)
            return Conflict(new { message = "The administrator is configured on the server. Update Namerre:Authentication:PasswordHash instead." });
        if (result != PasswordChangeResult.Changed || changed is null)
            return Conflict(new { message = "The stored administrator changed unexpectedly. Restart Namerre and sign in again." });

        logger.LogInformation("Administrator password changed.");
        // The credential stamp changed, which signs out every other session; keep this one.
        await SignInAsync(changed);
        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var endSessionUrl = IsSingleSignOnSession && oidc.Enabled
            ? await EndSessionUrlAsync(await HttpContext.GetTokenAsync(Authentication.OidcLogin.IdTokenName),
                cancellationToken)
            : null;
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // The browser must navigate to the provider itself, so the address is returned instead of a redirect.
        return endSessionUrl is null ? NoContent() : Ok(new { endSessionUrl });
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

    private bool IsSingleSignOnSession
        => User.FindFirst(Authentication.OidcLogin.MethodClaim)?.Value == Authentication.OidcLogin.Scheme;

    private Task SignInAsync(AdminCredentials credentials)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, credentials.Username),
            new Claim(ClaimTypes.Name, credentials.Username),
            new Claim(AdminAccount.StampClaim, AdminAccount.StampOf(credentials))
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });
    }
}

public sealed record LoginRequest(
    [Required, StringLength(128)] string Username,
    [Required, StringLength(1024)] string Password);

public sealed record SetupRequest(
    [Required, StringLength(64, MinimumLength = 64)] string Token,
    [Required, StringLength(128)] string Username,
    [Required, StringLength(1024)] string Password);

public sealed record ChangePasswordRequest(
    [Required, StringLength(1024)] string CurrentPassword,
    [Required, StringLength(1024)] string NewPassword);
