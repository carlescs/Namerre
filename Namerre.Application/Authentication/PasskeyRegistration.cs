namespace Namerre.Application.Authentication;

/// <summary>A started passkey registration for <see cref="User"/>, as it was when confirmed.</summary>
public sealed record PasskeyRegistration(UserRecord User, PasskeyChallenge Challenge);
