namespace Namerre.Application.Authentication;

/// <summary>A verified single sign-on identity: the provider's stable subject plus a username to show.</summary>
public sealed record ExternalIdentity(string Issuer, string Subject, string Username);
