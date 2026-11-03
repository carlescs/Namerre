namespace Namerre.Application.Authentication;

/// <summary>
/// Durable record of the sessions that must no longer be accepted. Session cookies are stateless, so this is what
/// lets sign-out end a session on the server. Reads are served from memory (this process owns the database
/// exclusively); writes are persisted so revocations survive a restart. Storage failures throw
/// <see cref="UserStorageException"/>.
/// </summary>
public interface ISessionRegistry
{
    /// <summary>True when the session was ended; cheap enough to call on every request.</summary>
    bool IsRevoked(string sessionId);

    /// <summary>
    /// Ends one session. The entry is kept for <see cref="SessionPolicy.Lifetime"/>, after which the cookie is
    /// expired anyway, and then pruned. Never fails: when the write fails the session stays ended until the
    /// process restarts.
    /// </summary>
    void Revoke(string sessionId);

    /// <summary>
    /// The user's session epoch: sessions issued with a lower one are no longer valid. Works for every user,
    /// including the configured administrator, which is never stored.
    /// </summary>
    long Epoch(long userId);

    /// <summary>Raises the user's session epoch and returns the new one, ending every session issued before.</summary>
    long AdvanceEpoch(long userId);
}
