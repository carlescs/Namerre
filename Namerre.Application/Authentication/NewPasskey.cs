namespace Namerre.Application.Authentication;

/// <summary>A verified passkey registration, ready to store.</summary>
public sealed record NewPasskey(byte[] CredentialId, byte[] PublicKey, byte[] UserHandle, uint SignCount);
