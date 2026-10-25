using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namerre.Authentication;
using Namerre.Application.Authentication;
using Namerre.Application.Renaming;

namespace Namerre.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AdminAccount account, IAntiforgery antiforgery,
    ILogger<AuthController> logger) : ControllerBase
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
            canChangePassword = account.CanChangePassword
        });
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
        var verified = account.Authenticate(request.Username, request.Password);
        if (verified is null)
            return Unauthorized(new { message = "Invalid username or password." });

        await SignInAsync(verified);
        return NoContent();
    }

    [EnableRateLimiting("login")]
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
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
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

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
