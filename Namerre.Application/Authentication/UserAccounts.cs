using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Namerre.Application.Authentication;

/// <summary>
/// Local users and the rules that keep them usable: unique usernames, the password policy, at least one enabled
/// administrator, and at least one sign-in method for every enabled user. Every change that affects a user's access
/// replaces their credential stamp, which ends their existing sessions.
/// </summary>
/// <remarks>
/// This process owns the database exclusively (see <c>RenameStore</c>), so users are cached in memory and reloaded
/// after every change; mutations are serialized under one lock and written compare-and-swap on the stamp.
/// An administrator configured under <c>Namerre:Authentication</c> is kept as a break-glass account with ID
/// <see cref="ConfiguredUserId"/>; it is never stored and cannot be changed here.
/// </remarks>
public sealed class UserAccounts
{
    public const long ConfiguredUserId = 0;
    public const int MaximumUsernameLength = 128;
    private readonly object gate = new();
    private readonly IUserStore store;
    private readonly IPasswordHasher passwords;
    private readonly TimeProvider time;
    private readonly ILogger<UserAccounts> logger;
    private readonly UserRecord? configured;
    private IReadOnlyDictionary<long, UserRecord> users;

    public UserAccounts(IAdministratorConfiguration administrator, IUserStore store, IPasswordHasher passwords,
        ILogger<UserAccounts> logger, TimeProvider? time = null)
    {
        this.store = store;
        this.passwords = passwords;
        this.logger = logger;
        this.time = time ?? TimeProvider.System;
        var username = administrator.Username;
        var passwordHash = administrator.PasswordHash;
        if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(passwordHash))
        {
            if (!IsValidUsername(username) || !passwords.IsValidHash(passwordHash))
                throw new InvalidOperationException(
                    "The administrator credentials are invalid. Configure Namerre:Authentication:Username and Namerre:Authentication:PasswordHash, or restore valid administrator data.");
            configured = new UserRecord(ConfiguredUserId, username!.Trim(), passwordHash, UserRole.Admin, false,
                ConfiguredStamp(username.Trim(), passwordHash!), DateTimeOffset.UnixEpoch, null, []);
        }

        users = Load();
        if (!RequiresSetup)
            return;
        SetupToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        // Written to the console only, never to the logs, which may be shipped elsewhere.
        Console.Error.WriteLine($"Namerre installation: open the app over HTTPS and enter this one-time setup token: {SetupToken}");
    }

    public bool RequiresSetup => configured is null && Volatile.Read(ref users).Count == 0;
    public bool HasConfiguredAdministrator => configured is not null;
    /// <summary>
    /// The one-time installation token printed to the console while <see cref="RequiresSetup"/>. Public for the
    /// host's tests only: never return it from an endpoint or write it to the logs.
    /// </summary>
    public string? SetupToken { get; private set; }

    public UserRecord? Find(long id)
        => id == ConfiguredUserId ? configured : Volatile.Read(ref users).GetValueOrDefault(id);

    /// <summary>Every user, the configured administrator first.</summary>
    public IReadOnlyList<UserRecord> List()
        => [.. configured is null ? [] : new[] { configured }, .. Volatile.Read(ref users).Values.OrderBy(u => u.Id)];

    public SetupResult CompleteSetup(string token, string username, string password)
    {
        lock (gate)
        {
            if (!RequiresSetup)
                return SetupResult.AlreadyConfigured;
            if (SetupToken is null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(SetupToken), Encoding.UTF8.GetBytes(token)))
                return SetupResult.InvalidToken;
            if (!PasswordPolicy.IsAcceptable(password))
                return SetupResult.WeakPassword;
            if (!IsValidUsername(username))
                return SetupResult.InvalidUsername;
            if (!store.TryCreate(NewUser(username.Trim(), UserRole.Admin, passwords.Hash(password)), out _))
                return SetupResult.AlreadyConfigured;
            Reload();
            SetupToken = null;
            return SetupResult.Completed;
        }
    }

    /// <summary>
    /// Returns the user whose username and password match, or null. Unknown users, users without a password and
    /// disabled users fail exactly like a wrong password, after the same hash check.
    /// </summary>
    public UserRecord? Authenticate(string username, string password)
    {
        var user = List().FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.Ordinal));
        var verified = passwords.Verify(user?.PasswordHash, password);
        return verified && user is { Disabled: false } ? user : null;
    }

    /// <summary>Checks the password of a known user, e.g. before a sensitive change.</summary>
    public bool VerifyPassword(UserRecord user, string? password)
        => password is not null && passwords.Verify(user.PasswordHash, password);

    /// <summary>
    /// Confirms it is really the user before a sensitive change to their own account: a recent sign-in, or their
    /// password when they have one. Returns the user, who must be editable here.
    /// </summary>
    public UserRecord ConfirmOwnChange(long id, bool recentSignIn, string? password)
    {
        var user = Editable(id);
        if (!recentSignIn && !VerifyPassword(user, password))
            throw AccountException.RecentSignIn();
        return user;
    }

    public void RecordSignIn(UserRecord user, long? loginId = null)
    {
        if (user.Id == ConfiguredUserId)
            return;
        try
        {
            store.RecordSignIn(user.Id, loginId, time.GetUtcNow());
            lock (gate)
                Reload();
        }
        catch (UserStorageException exception)
        {
            // Best effort: the sign-in itself already succeeded.
            logger.LogWarning(exception, "Unable to record the sign-in of user {UserId}.", user.Id);
        }
    }

    /// <summary>Resolves a single sign-on identity to its linked user, creating one first when allowed.</summary>
    public (UserRecord? User, long? LoginId, ExternalSignInFailure Failure) SignInExternal(ExternalIdentity identity,
        bool autoProvision)
    {
        lock (gate)
        {
            var linked = FindLogin(identity.Issuer, identity.Subject);
            if (linked is { } found)
                return found.User.Disabled
                    ? (null, null, ExternalSignInFailure.Disabled)
                    : (found.User, found.Login.Id, ExternalSignInFailure.None);
            // Never attach an identity to an existing user by username or email: linking is always explicit.
            if (!autoProvision)
                return (null, null, ExternalSignInFailure.NotLinked);
            // A provisioned user would end installation, leaving no way to create the first administrator.
            if (RequiresSetup)
                return (null, null, ExternalSignInFailure.SetupRequired);
            try
            {
                var now = time.GetUtcNow();
                for (var attempt = 1; attempt <= 20; attempt++)
                {
                    var username = ProvisionedUsername(identity, attempt);
                    var user = NewUser(username, UserRole.User, null) with
                    {
                        Logins = [new UserLoginRecord(0, 0, identity.Issuer, identity.Subject,
                            Truncate(identity.Username, 256), now, null)]
                    };
                    if (IsTaken(username))
                        continue;
                    if (!store.TryCreate(user, out var created))
                    {
                        Reload();
                        // Someone else linked the identity meanwhile.
                        if (FindLogin(identity.Issuer, identity.Subject) is { } raced)
                            return raced.User.Disabled
                                ? (null, null, ExternalSignInFailure.Disabled)
                                : (raced.User, raced.Login.Id, ExternalSignInFailure.None);
                        continue;
                    }
                    Reload();
                    logger.LogInformation(
                        "Created user {UserId} ({Username}) for a first single sign-on from issuer {Issuer}.",
                        created.Id, LogText.Clean(created.Username), LogText.Clean(identity.Issuer));
                    return (created, created.Logins[0].Id, ExternalSignInFailure.None);
                }
                return (null, null, ExternalSignInFailure.Unavailable);
            }
            catch (UserStorageException exception)
            {
                logger.LogError(exception, "Unable to create a user for single sign-on.");
                return (null, null, ExternalSignInFailure.Unavailable);
            }
        }
    }

    public UserRecord Create(UserReference actor, string username, UserRole role, string? password,
        ExternalIdentity? login)
    {
        if (!IsValidUsername(username))
            throw InvalidUsername();
        if (password is null && login is null)
            throw AccountException.LastSignInMethod();
        if (password is not null && !PasswordPolicy.IsAcceptable(password))
            throw WeakPassword();
        var hash = password is null ? null : passwords.Hash(password);
        lock (gate)
        {
            username = username.Trim();
            if (IsTaken(username))
                throw UsernameTaken();
            if (login is not null && FindLogin(login.Issuer, login.Subject) is not null)
                throw AccountException.LoginTaken();
            var user = NewUser(username, role, hash);
            if (login is not null)
                user = user with
                {
                    Logins = [new UserLoginRecord(0, 0, login.Issuer, login.Subject, login.Username,
                        time.GetUtcNow(), null)]
                };
            if (!store.TryCreate(user, out var created))
            {
                Reload();
                throw login is not null && FindLogin(login.Issuer, login.Subject) is not null
                    ? AccountException.LoginTaken()
                    : UsernameTaken();
            }
            Reload();
            logger.LogInformation("{Actor} created user {UserId} ({Username}) with role {Role}.", LogText.Clean(actor.Username),
                created.Id, LogText.Clean(created.Username), created.Role);
            return created;
        }
    }

    /// <summary>Changes a user's role or disabled flag; either ends the user's sessions.</summary>
    public UserRecord Update(UserReference actor, long id, UserRole? role, bool? disabled)
    {
        lock (gate)
        {
            var user = Editable(id);
            var updated = user with { Role = role ?? user.Role, Disabled = disabled ?? user.Disabled };
            if (updated.Role == user.Role && updated.Disabled == user.Disabled)
                return user;
            if (actor.Id == id && (updated.Role != UserRole.Admin || updated.Disabled))
                throw new AccountException("OwnAccount",
                    "You cannot remove your own administrator role or disable yourself. Ask another administrator.",
                    409);
            if (IsActiveAdmin(user) && !IsActiveAdmin(updated))
                EnsureAnotherAdmin(id);
            if (!updated.Disabled && updated.SignInMethods == 0)
                throw AccountException.LastSignInMethod();
            Save(user, updated with { Stamp = NewStamp() });
            logger.LogInformation("{Actor} changed user {UserId} ({Username}): role {Role}, disabled {Disabled}.",
                LogText.Clean(actor.Username), id, LogText.Clean(user.Username), updated.Role, updated.Disabled);
            return Find(id)!;
        }
    }

    /// <summary>Sets a new password. Users with a password must confirm the current one unless an admin resets it.</summary>
    public UserRecord SetPassword(UserReference actor, long id, string? currentPassword, string newPassword,
        bool recentSignIn)
    {
        if (id == ConfiguredUserId && configured is not null)
            throw AccountException.Configured();
        if (!PasswordPolicy.IsAcceptable(newPassword))
            throw WeakPassword();
        var hash = passwords.Hash(newPassword);
        lock (gate)
        {
            var user = Editable(id);
            var reset = actor.Id != id;
            if (!reset)
            {
                if (user.HasPassword && !VerifyPassword(user, currentPassword))
                    throw new AccountException("InvalidPassword", "The current password is incorrect.", 403);
                // Without a password to confirm, a recent sign-in shows it is really this user.
                if (!user.HasPassword && !recentSignIn)
                    throw AccountException.RecentSignIn();
            }
            Save(user, user with { PasswordHash = hash, Stamp = NewStamp() });
            if (reset)
                logger.LogInformation("{Actor} reset the password of user {UserId} ({Username}).", LogText.Clean(actor.Username), id,
                    LogText.Clean(user.Username));
            else
                logger.LogInformation("User {UserId} ({Username}) {Action} their password.", id, LogText.Clean(user.Username),
                    user.HasPassword ? "changed" : "set");
            return Find(id)!;
        }
    }

    public UserRecord RemovePassword(UserReference actor, long id)
    {
        lock (gate)
        {
            var user = Editable(id);
            if (!user.HasPassword)
                return user;
            if (!user.Disabled && user.Logins.Count == 0)
                throw AccountException.LastSignInMethod();
            Save(user, user with { PasswordHash = null, Stamp = NewStamp() });
            logger.LogInformation("{Actor} removed the password of user {UserId} ({Username}).", LogText.Clean(actor.Username), id,
                LogText.Clean(user.Username));
            return Find(id)!;
        }
    }

    /// <summary>Links an identity to a user. <paramref name="expectedStamp"/> pins the user version that asked.</summary>
    public UserRecord Link(UserReference actor, long id, ExternalIdentity identity, string? expectedStamp = null)
    {
        lock (gate)
        {
            var user = Editable(id);
            if (expectedStamp is not null && user.Stamp != expectedStamp)
                throw AccountException.Changed();
            if (FindLogin(identity.Issuer, identity.Subject) is { } existing)
                throw existing.User.Id == id
                    ? new AccountException("AlreadyLinked", "This identity is already linked to this user.", 409)
                    : AccountException.LoginTaken();
            var login = new UserLoginRecord(0, id, identity.Issuer, identity.Subject,
                Truncate(identity.Username, 256), time.GetUtcNow(), null);
            if (!store.TryAddLogin(id, user.Stamp, NewStamp(), login))
            {
                Reload();
                throw FindLogin(identity.Issuer, identity.Subject) is not null
                    ? AccountException.LoginTaken()
                    : AccountException.Changed();
            }
            Reload();
            logger.LogInformation("{Actor} linked a single sign-on identity from issuer {Issuer} to user {UserId} ({Username}).",
                LogText.Clean(actor.Username), LogText.Clean(identity.Issuer), id, LogText.Clean(user.Username));
            return Find(id)!;
        }
    }

    public UserRecord Unlink(UserReference actor, long id, long loginId)
    {
        lock (gate)
        {
            var user = Editable(id);
            var login = user.Logins.FirstOrDefault(l => l.Id == loginId)
                ?? throw new AccountException("LoginNotFound", "The identity is not linked to this user.", 404);
            if (!user.Disabled && user.SignInMethods == 1)
                throw AccountException.LastSignInMethod();
            if (!store.TryRemoveLogin(id, user.Stamp, NewStamp(), loginId))
            {
                Reload();
                throw AccountException.Changed();
            }
            Reload();
            logger.LogInformation("{Actor} unlinked a single sign-on identity from issuer {Issuer} from user {UserId} ({Username}).",
                LogText.Clean(actor.Username), LogText.Clean(login.Issuer), id, LogText.Clean(user.Username));
            return Find(id)!;
        }
    }

    /// <summary>
    /// Moves a link to another user, e.g. from an automatically created user to the right one. When the source has
    /// no other way to sign in it must be deleted (<paramref name="deleteSource"/>), so no enabled user is left
    /// without a sign-in method.
    /// </summary>
    public UserRecord MoveLogin(UserReference actor, long loginId, long toUserId, bool deleteSource)
    {
        lock (gate)
        {
            var source = Volatile.Read(ref users).Values.FirstOrDefault(u => u.Logins.Any(l => l.Id == loginId))
                ?? throw new AccountException("LoginNotFound", "The identity is not linked to any user.", 404);
            var target = Editable(toUserId);
            if (source.Id == target.Id)
                throw new AccountException("AlreadyLinked", "This identity is already linked to this user.", 409);
            if (deleteSource)
                EnsureDeletable(actor, source);
            else if (!source.Disabled && source.SignInMethods == 1)
                throw AccountException.LastSignInMethod();
            if (!store.TryMoveLogin(loginId, source.Id, source.Stamp, target.Id, target.Stamp, NewStamp(), deleteSource))
            {
                Reload();
                throw AccountException.Changed();
            }
            Reload();
            logger.LogInformation(
                "{Actor} moved a single sign-on identity from user {SourceId} ({SourceName}) to user {UserId} ({Username}){Deleted}.",
                LogText.Clean(actor.Username), source.Id, LogText.Clean(source.Username), target.Id, LogText.Clean(target.Username),
                deleteSource ? " and deleted the source user" : "");
            return Find(toUserId)!;
        }
    }

    /// <summary>The user an identity is linked to, or null.</summary>
    public UserRecord? OwnerOfLogin(long loginId)
        => Volatile.Read(ref users).Values.FirstOrDefault(u => u.Logins.Any(l => l.Id == loginId));

    /// <summary>
    /// Merges <paramref name="sourceId"/> into <paramref name="targetId"/>, e.g. a user created automatically on
    /// first sign-in into the person's existing user: every identity of the source moves to the target and the
    /// source is deleted with its password. The target keeps its own role and password. The caller re-attributes
    /// the source's history (<see cref="IUserHistory"/>). Returns the merged target.
    /// </summary>
    public UserRecord Merge(UserReference actor, long sourceId, long targetId)
    {
        lock (gate)
        {
            var target = Editable(targetId);
            var source = Editable(sourceId);
            if (source.Id == target.Id)
                throw new AccountException("SameUser", "Choose two different users to merge.", 409);
            EnsureDeletable(actor, source);
            if (!store.TryMergeUser(source.Id, source.Stamp, target.Id, target.Stamp, NewStamp()))
            {
                Reload();
                throw AccountException.Changed();
            }
            Reload();
            logger.LogInformation(
                "{Actor} merged user {SourceId} ({SourceName}) with {LoginCount} single sign-on identities into user {UserId} ({Username}).",
                LogText.Clean(actor.Username), source.Id, LogText.Clean(source.Username), source.Logins.Count, target.Id, LogText.Clean(target.Username));
            return Find(targetId)!;
        }
    }

    /// <summary>Deletes a user and returns it as it was.</summary>
    public UserRecord Delete(UserReference actor, long id)
    {
        lock (gate)
        {
            var user = Editable(id);
            EnsureDeletable(actor, user);
            if (!store.TryDelete(id, user.Stamp))
            {
                Reload();
                throw AccountException.Changed();
            }
            Reload();
            logger.LogInformation("{Actor} deleted user {UserId} ({Username}).", LogText.Clean(actor.Username), id, LogText.Clean(user.Username));
            return user;
        }
    }

    private void EnsureDeletable(UserReference actor, UserRecord user)
    {
        if (user.Id == ConfiguredUserId)
            throw AccountException.Configured();
        if (actor.Id == user.Id)
            throw new AccountException("OwnAccount", "You cannot delete your own account.", 409);
        if (IsActiveAdmin(user))
            EnsureAnotherAdmin(user.Id);
    }

    private void EnsureAnotherAdmin(long id)
    {
        if (!List().Any(u => u.Id != id && IsActiveAdmin(u)))
            throw new AccountException("LastAdmin", "Keep at least one enabled administrator.", 409);
    }

    private static bool IsActiveAdmin(UserRecord user) => user is { Role: UserRole.Admin, Disabled: false };

    private UserRecord Editable(long id)
    {
        if (id == ConfiguredUserId && configured is not null)
            throw AccountException.Configured();
        return Volatile.Read(ref users).GetValueOrDefault(id) ?? throw AccountException.NotFound();
    }

    private void Save(UserRecord current, UserRecord updated)
    {
        if (!store.TryUpdate(updated, current.Stamp))
        {
            Reload();
            throw AccountException.Changed();
        }
        Reload();
    }

    private (UserRecord User, UserLoginRecord Login)? FindLogin(string issuer, string subject)
    {
        foreach (var user in Volatile.Read(ref users).Values)
            foreach (var login in user.Logins)
                if (login.Issuer == issuer && login.Subject == subject)
                    return (user, login);
        return null;
    }

    private bool IsTaken(string username)
        // Matches the database's NOCASE collation, which folds ASCII letters only.
        => List().Any(u => AsciiEquals(u.Username, username));

    private static bool AsciiEquals(string a, string b)
        => a.Length == b.Length && a.Zip(b).All(pair => char.IsAsciiLetter(pair.First) && char.IsAsciiLetter(pair.Second)
            ? char.ToLowerInvariant(pair.First) == char.ToLowerInvariant(pair.Second)
            : pair.First == pair.Second);

    private static string ProvisionedUsername(ExternalIdentity identity, int attempt)
    {
        var name = new string(identity.Username.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
            name = "user";
        var suffix = attempt == 1 ? "" : "-" + attempt;
        if (attempt == 20)
            suffix = "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Subject)))[..12];
        return Truncate(name, MaximumUsernameLength - suffix.Length) + suffix;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private UserRecord NewUser(string username, UserRole role, string? passwordHash)
        => new(0, username, passwordHash, role, false, NewStamp(), time.GetUtcNow(), null, []);

    private void Reload() => Volatile.Write(ref users, Load());

    private IReadOnlyDictionary<long, UserRecord> Load() => store.List().ToDictionary(u => u.Id);

    public static bool IsValidUsername(string? username)
        => !string.IsNullOrWhiteSpace(username) && username.Trim().Length <= MaximumUsernameLength &&
            !username.Any(char.IsControl);

    private static string NewStamp() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    // The configured administrator's stamp follows its settings, so changing them ends its sessions.
    private static string ConfiguredStamp(string username, string passwordHash) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(username + "\0" + passwordHash)));

    private static AccountException InvalidUsername() => new("InvalidUsername",
        $"Enter a username of 1 to {MaximumUsernameLength} characters without control characters.");

    private static AccountException UsernameTaken() => new("UsernameTaken", "That username is already taken.", 409);

    private static AccountException WeakPassword() => new("WeakPassword", PasswordPolicy.WeakPasswordMessage);
}
