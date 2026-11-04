using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Namerre.Application.Authentication;

namespace Namerre.Authentication;

/// <summary>
/// The device cookie: issued on every successful password or passkey sign-in, it lets this browser keep signing in
/// as that user while their account is slowed down by failed guesses from elsewhere (<see cref="SignInThrottle"/>).
/// It holds only the user ID and a random device ID, encrypted and signed, and grants no access by itself.
/// </summary>
public sealed class TrustedDevices(IDataProtectionProvider dataProtection, TimeProvider? time = null)
{
    public const string CookieName = "__Host-namerre-device";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);
    private readonly ITimeLimitedDataProtector protector = dataProtection
        .CreateProtector("Namerre.Authentication.TrustedDevice").ToTimeLimitedDataProtector();
    private readonly TimeProvider time = time ?? TimeProvider.System;

    /// <summary>The device the request's cookie names, or null when it has none or it is not valid.</summary>
    public TrustedDevice? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value) || value.Length > 1024)
            return null;
        try
        {
            var parts = protector.Unprotect(value, out var expiration).Split('|');
            return expiration > time.GetUtcNow() && parts.Length == 2 &&
                long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var userId) &&
                parts[1].Length > 0
                ? new TrustedDevice(userId, parts[1])
                : null;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Remembers this browser for the user, keeping its device ID when it already had one for them.</summary>
    public void Issue(HttpContext context, long userId)
    {
        var deviceId = Read(context.Request) is { } current && current.UserId == userId
            ? current.Id
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var expires = time.GetUtcNow() + Lifetime;
        var value = protector.Protect(string.Join('|', userId.ToString(CultureInfo.InvariantCulture), deviceId),
            expires);
        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expires,
            IsEssential = true
        });
    }
}
