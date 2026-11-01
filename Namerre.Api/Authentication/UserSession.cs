using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Namerre.Application.Authentication;

namespace Namerre.Authentication;

/// <summary>
/// Builds and reads the session cookie's principal. Every session belongs to a local user: its ID, username, role and
/// credential stamp come from the user record, never from an identity provider's claims.
/// </summary>
public static class UserSession
{
    public const string StampClaim = "credential-stamp";
    public const string MethodClaim = "auth-method";
    public const string PasswordMethod = "password";
    public const string SingleSignOnMethod = "oidc";
    /// <summary>The single sign-on settings stamp (see <see cref="OidcLogin.Stamp"/>) on single sign-on sessions.</summary>
    public const string OidcStampClaim = "oidc-stamp";
    public const string SignedInAtClaim = "signed-in-at";
    /// <summary>How long after signing in sensitive account changes are allowed without confirming again.</summary>
    public static readonly TimeSpan RecentSignIn = TimeSpan.FromMinutes(10);

    public static ClaimsPrincipal Create(UserRecord user, string method, string? oidcStamp, DateTimeOffset signedInAt)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(StampClaim, user.Stamp),
            new(MethodClaim, method),
            new(SignedInAtClaim, signedInAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        ];
        if (oidcStamp is not null)
            claims.Add(new(OidcStampClaim, oidcStamp));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>The same session for a changed user record, keeping its method and sign-in time.</summary>
    public static ClaimsPrincipal Renew(ClaimsPrincipal current, UserRecord user, DateTimeOffset? signedInAt = null)
        => Create(user, Method(current), current.FindFirst(OidcStampClaim)?.Value,
            signedInAt ?? SignedInAt(current) ?? DateTimeOffset.UnixEpoch);

    public static long? UserId(ClaimsPrincipal principal)
        => long.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var id) ? id : null;

    public static UserReference? Reference(ClaimsPrincipal principal)
        => principal.Identity?.IsAuthenticated == true && UserId(principal) is { } id
            ? new UserReference(id, principal.Identity.Name ?? "")
            : null;

    public static string Method(ClaimsPrincipal principal)
        => principal.FindFirst(MethodClaim)?.Value ?? PasswordMethod;

    public static bool IsSingleSignOn(ClaimsPrincipal principal) => Method(principal) == SingleSignOnMethod;

    public static DateTimeOffset? SignedInAt(ClaimsPrincipal principal)
        => long.TryParse(principal.FindFirst(SignedInAtClaim)?.Value, NumberStyles.None, CultureInfo.InvariantCulture,
            out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    public static bool IsRecent(ClaimsPrincipal principal, DateTimeOffset now)
        => SignedInAt(principal) is { } at && now - at <= RecentSignIn && at <= now + TimeSpan.FromMinutes(1);

    /// <summary>The live-update group of a session; changes to the user or the SSO settings leave it.</summary>
    public static string Group(ClaimsPrincipal principal)
        => Group(principal.FindFirst(StampClaim)?.Value ?? "", principal.FindFirst(OidcStampClaim)?.Value);

    public static string Group(string userStamp, string? oidcStamp)
        => "stamp:" + userStamp + (oidcStamp is null ? "" : "|" + oidcStamp);
}
