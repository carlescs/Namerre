namespace Namerre.Application.Authentication;

/// <summary>Who passkeys are for: the WebAuthn relying party ID (a host name) and the browser origin that asks.</summary>
public sealed record PasskeyRelyingParty(string Id, string Origin)
{
    public const string Name = "Namerre";
}
