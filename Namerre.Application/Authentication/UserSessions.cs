using Microsoft.Extensions.Logging;

namespace Namerre.Application.Authentication;

/// <summary>
/// Server-side control of sign-in sessions. A session carries a random ID and the user's session epoch from when it
/// was issued; it stops being valid once its ID is revoked (sign-out) or the user's epoch moves on ("sign out of
/// other sessions"). Credential changes end sessions separately, through the credential stamp.
/// </summary>
public sealed class UserSessions(ISessionRegistry registry, ILogger<UserSessions> logger)
{
    /// <summary>The epoch a new session is issued with.</summary>
    public long CurrentEpoch(long userId) => registry.Epoch(userId);

    /// <summary>
    /// False for a session that was signed out or issued before the user ended their other sessions. A session
    /// without an ID predates server-side sessions and is not accepted.
    /// </summary>
    public bool IsActive(string? sessionId, long userId, long sessionEpoch)
        => !string.IsNullOrEmpty(sessionId) && sessionEpoch >= registry.Epoch(userId) && !registry.IsRevoked(sessionId);

    /// <summary>Ends one session so a copy of its cookie stops working at once.</summary>
    public void End(UserReference user, string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;
        registry.Revoke(sessionId);
        logger.LogInformation("User {UserId} ({Username}) signed out.", user.Id, LogText.Clean(user.Username));
    }

    /// <summary>
    /// Ends every session of the user issued so far and returns the epoch that new sessions, such as the one that
    /// asked, must be issued with.
    /// </summary>
    public long EndAll(UserReference user)
    {
        var epoch = registry.AdvanceEpoch(user.Id);
        logger.LogInformation("User {UserId} ({Username}) signed out of all other sessions.", user.Id,
            LogText.Clean(user.Username));
        return epoch;
    }
}
