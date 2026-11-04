namespace Namerre.Application.Authentication;

/// <summary>A browser a user signed in from before, identified by a cookie only the server can issue.</summary>
public sealed record TrustedDevice(long UserId, string Id);
