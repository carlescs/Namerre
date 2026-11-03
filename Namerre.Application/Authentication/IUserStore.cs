namespace Namerre.Application.Authentication;

/// <summary>
/// Durable storage for local users, their single sign-on links and their passkeys. Writes are compare-and-swap on the user's
/// <see cref="UserRecord.Stamp"/> where noted, so a caller never overwrites a change it did not see.
/// Storage failures throw <see cref="UserStorageException"/>.
/// </summary>
public interface IUserStore
{
    /// <summary>False when no data directory is configured; reads then return nothing and writes throw.</summary>
    bool IsAvailable { get; }
    IReadOnlyList<UserRecord> List();
    /// <summary>Adds a user (and optionally its first link); false when the username or the link is taken.</summary>
    bool TryCreate(UserRecord user, out UserRecord created);
    /// <summary>Replaces the user's own fields when its stamp is still <paramref name="expectedStamp"/>.</summary>
    bool TryUpdate(UserRecord updated, string expectedStamp);
    /// <summary>Links an identity; false when the user changed or the identity is linked to anyone already.</summary>
    bool TryAddLogin(long userId, string expectedStamp, string newStamp, UserLoginRecord login);
    /// <summary>Unlinks an identity from the user; false when either changed.</summary>
    bool TryRemoveLogin(long userId, string expectedStamp, string newStamp, long loginId);
    /// <summary>Registers a passkey; false when the user changed or the credential is registered to anyone already.</summary>
    bool TryAddPasskey(long userId, string expectedStamp, string newStamp, UserPasskeyRecord passkey);
    /// <summary>
    /// Renames a passkey of the user. The name is not a sign-in method, so the stamp stays and sessions continue;
    /// false when the passkey is not the user's (any more).
    /// </summary>
    bool TryRenamePasskey(long userId, long passkeyId, string name);
    /// <summary>Removes a passkey from the user; false when either changed.</summary>
    bool TryRemovePasskey(long userId, string expectedStamp, string newStamp, long passkeyId);
    /// <summary>
    /// Records a passkey sign-in: the user's sign-in time, and the passkey's use and new signature counter. False,
    /// recording nothing, when the passkey is gone or its counter is no longer <paramref name="expectedSignCount"/>
    /// (the one the sign-in was verified against), so a counter never moves backwards.
    /// </summary>
    bool TryRecordPasskeySignIn(long userId, long passkeyId, uint expectedSignCount, uint signCount,
        DateTimeOffset at);
    /// <summary>Moves a link to another user, optionally deleting the user it came from, in one transaction.</summary>
    bool TryMoveLogin(long loginId, long fromUserId, string fromStamp, long toUserId, string toStamp,
        string newStamp, bool deleteSource);
    /// <summary>
    /// Merges one user into another in one transaction: every link and passkey of the source moves to the target, the target is
    /// restamped and the source is deleted (with its password). False when either user changed.
    /// </summary>
    bool TryMergeUser(long fromUserId, string fromStamp, long toUserId, string toStamp, string newStamp);
    bool TryDelete(long userId, string expectedStamp);
    void RecordSignIn(long userId, long? loginId, DateTimeOffset at);
}

public sealed class UserStorageException(string message, Exception? innerException = null)
    : Exception(message, innerException);
