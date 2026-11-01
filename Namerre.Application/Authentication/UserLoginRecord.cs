namespace Namerre.Application.Authentication;

/// <summary>A single sign-on identity linked to a local user, keyed by the provider's stable subject.</summary>
/// <param name="DisplayName">The identity's username when it was linked, for people to recognise it.</param>
public sealed record UserLoginRecord(long Id, long UserId, string Issuer, string Subject, string DisplayName,
    DateTimeOffset LinkedAt, DateTimeOffset? LastUsedAt);
