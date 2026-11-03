using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Authentication;

/// <summary>
/// The public HTTPS origin people open Namerre at, pinned by the operator instead of read from each request's
/// <c>Host</c> header, which a client controls. <c>Namerre:PublicOrigin</c> is the general setting;
/// <c>Namerre:Authentication:Passkeys:Origin</c> overrides it for passkeys only. Each falls back to the other.
/// </summary>
public sealed class PublicOrigin
{
    public PublicOrigin(IOptions<NamerreOptions> options)
    {
        var settings = options.Value;
        var general = Parse(settings.PublicOrigin, "Namerre:PublicOrigin");
        var passkeys = Parse(settings.Authentication.Passkeys.Origin, "Namerre:Authentication:Passkeys:Origin");
        General = general ?? passkeys;
        Passkeys = passkeys ?? general;
    }

    /// <summary>The origin for links back to Namerre (such as the post-logout return address), or null.</summary>
    public PasskeyRelyingParty? General { get; }

    /// <summary>The relying party passkey ceremonies are pinned to, or null.</summary>
    public PasskeyRelyingParty? Passkeys { get; }

    /// <summary>Validates one origin setting; null when it is empty, an error naming the setting when it is invalid.</summary>
    private static PasskeyRelyingParty? Parse(string? value, string setting)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var origin) ||
            origin.Scheme != Uri.UriSchemeHttps && !(origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback) ||
            origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) ||
            !string.IsNullOrEmpty(origin.UserInfo) || origin.HostNameType != UriHostNameType.Dns)
            throw new InvalidOperationException(
                $"{setting} must be an HTTPS origin with a host name, e.g. https://namerre.example.com.");
        return new PasskeyRelyingParty(origin.IdnHost, origin.GetLeftPart(UriPartial.Authority));
    }
}
