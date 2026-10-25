using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Authentication;

/// <summary>Optional OpenID Connect sign-in. Users holding the required role become the administrator.</summary>
public sealed class OidcLogin
{
    public const string Scheme = "oidc";
    public const string MethodClaim = "auth-method";
    public const string CallbackPath = "/api/auth/oidc/callback";

    public OidcLogin(IOptions<NamerreOptions> options)
    {
        Options = options.Value.Authentication.Oidc;
        Enabled = !string.IsNullOrWhiteSpace(Options.Authority);
        if (!Enabled)
        {
            if (!string.IsNullOrWhiteSpace(Options.ClientId) || !string.IsNullOrWhiteSpace(Options.ClientSecret) ||
                !string.IsNullOrWhiteSpace(Options.RequiredRole))
                throw Invalid("Namerre:Authentication:Oidc:Authority is required when other OIDC settings are set.");
            return;
        }

        if (!Uri.TryCreate(Options.Authority, UriKind.Absolute, out var authority) ||
            authority.Scheme != Uri.UriSchemeHttps && (Options.RequireHttpsMetadata || authority.Scheme != Uri.UriSchemeHttp))
            throw Invalid("Namerre:Authentication:Oidc:Authority must be an absolute HTTPS URL.");
        if (string.IsNullOrWhiteSpace(Options.ClientId))
            throw Invalid("Namerre:Authentication:Oidc:ClientId is required.");
        // Fail closed: without a required role every account of the identity provider would be an administrator.
        if (string.IsNullOrWhiteSpace(Options.RequiredRole))
            throw Invalid("Namerre:Authentication:Oidc:RequiredRole is required.");
        if (string.IsNullOrWhiteSpace(Options.RoleClaim) || Options.RoleClaim.Split('.').Any(string.IsNullOrEmpty))
            throw Invalid("Namerre:Authentication:Oidc:RoleClaim is invalid.");
        if (string.IsNullOrWhiteSpace(Options.UsernameClaim))
            throw Invalid("Namerre:Authentication:Oidc:UsernameClaim is required.");
        if (string.IsNullOrWhiteSpace(Options.DisplayName) || Options.DisplayName.Length > 64)
            throw Invalid("Namerre:Authentication:Oidc:DisplayName must contain 1 to 64 characters.");

        // Changing who may sign in invalidates existing single sign-on sessions.
        Stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0",
            Scheme, Options.Authority, Options.ClientId, Options.RequiredRole, Options.RoleClaim, Options.UsernameClaim))));
    }

    public OidcOptions Options { get; }
    public bool Enabled { get; }
    public string DisplayName => Options.DisplayName ?? "";
    public string Stamp { get; } = "";

    /// <summary>Returns the session principal for a validated identity, or null when the user may not sign in.</summary>
    public ClaimsPrincipal? CreateSessionPrincipal(ClaimsPrincipal? external)
    {
        if (!Enabled || external is null || !HasRole(external, Options.RoleClaim!, Options.RequiredRole!))
            return null;
        var subject = external.FindFirst("sub")?.Value;
        var username = external.FindFirst(Options.UsernameClaim!)?.Value;
        if (string.IsNullOrWhiteSpace(username))
            username = subject;
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(username) || username.Length > 256)
            return null;
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Name, username),
            new Claim(MethodClaim, Scheme),
            new Claim(AdminAccount.StampClaim, Stamp)
        ], CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public bool IsValidSession(ClaimsPrincipal principal)
        => Enabled && principal.FindFirst(AdminAccount.StampClaim)?.Value == Stamp;

    /// <summary>
    /// Checks <paramref name="claimPath"/> for <paramref name="role"/>. The first segment is the claim type;
    /// further segments navigate its JSON value, e.g. Keycloak's <c>realm_access.roles</c>.
    /// </summary>
    public static bool HasRole(ClaimsPrincipal principal, string claimPath, string role)
    {
        var segments = claimPath.Split('.');
        foreach (var claim in principal.FindAll(segments[0]))
        {
            if (segments.Length == 1 && string.Equals(claim.Value, role, StringComparison.Ordinal))
                return true;
            if (claim.Value is not ['{', ..] and not ['[', ..])
                continue;
            try
            {
                using var document = JsonDocument.Parse(claim.Value);
                var element = document.RootElement;
                var found = true;
                foreach (var segment in segments.Skip(1))
                {
                    if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                    {
                        found = false;
                        break;
                    }
                }
                if (found && Contains(element, role))
                    return true;
            }
            catch (JsonException)
            {
            }
        }
        return false;
    }

    private static bool Contains(JsonElement element, string role) => element.ValueKind switch
    {
        JsonValueKind.String => element.ValueEquals(role),
        JsonValueKind.Array => element.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String && item.ValueEquals(role)),
        _ => false
    };

    private static InvalidOperationException Invalid(string message) => new(message);
}
