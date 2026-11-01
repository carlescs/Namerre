namespace Namerre.Application.Authentication;

/// <summary>
/// Durable storage for local users and their single sign-on links. Writes are compare-and-swap on the user's
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
    /// <summary>Moves a link to another user, optionally deleting the user it came from, in one transaction.</summary>
    bool TryMoveLogin(long loginId, long fromUserId, string fromStamp, long toUserId, string toStamp,
        string newStamp, bool deleteSource);
    bool TryDelete(long userId, string expectedStamp);
    void RecordSignIn(long userId, long? loginId, DateTimeOffset at);
}

public sealed class UserStorageException(string message, Exception? innerException = null)
    : Exception(message, innerException);
