namespace Namerre.Application.Authentication;

/// <summary>A passkey (WebAuthn public key credential) registered to a local user.</summary>
/// <param name="CredentialId">The authenticator's credential ID, unique across every user.</param>
/// <param name="PublicKey">The credential's COSE public key, which verifies its sign-in signatures.</param>
/// <param name="UserHandle">The WebAuthn user handle the credential was created for; sign-ins must return it.</param>
/// <param name="SignCount">The authenticator's last signature counter, used to detect cloned authenticators.</param>
/// <param name="Name">A name for people to recognise it, e.g. the device it lives on.</param>
public sealed record UserPasskeyRecord(long Id, long UserId, byte[] CredentialId, byte[] PublicKey,
    byte[] UserHandle, uint SignCount, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
