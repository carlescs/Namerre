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
            oidc = oidc.Enabled ? new { displayName = oidc.DisplayName } : null
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
    [EnableRateLimiting("login")]
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
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (!account.Verify(request.Username, request.Password))
            return Unauthorized(new { message = "Invalid username or password." });

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, account.Username),
            new Claim(ClaimTypes.Name, account.Username),
            new Claim(Authentication.OidcLogin.CredentialStampClaim, account.Stamp)
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });
        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

public sealed record LoginRequest(
    [Required, StringLength(128)] string Username,
    [Required, StringLength(1024)] string Password);

public sealed record SetupRequest(
    [Required, StringLength(64, MinimumLength = 64)] string Token,
    [Required, StringLength(128)] string Username,
    [Required, StringLength(1024)] string Password);
