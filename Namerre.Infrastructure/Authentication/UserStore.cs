using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Renaming;

namespace Namerre.Infrastructure.Authentication;

/// <summary>EF Core storage for local users, in the same SQLite database as the rename history.</summary>
public sealed class UserStore(RenameStore store) : IUserStore
{
    // SQLITE_CONSTRAINT_UNIQUE and SQLITE_CONSTRAINT_PRIMARYKEY.
    private const int UniqueViolation = 2067;
    private const int PrimaryKeyViolation = 1555;

    public bool IsAvailable => store.IsConfigured;

    public IReadOnlyList<UserRecord> List()
    {
        if (!IsAvailable)
            return [];
        // Read at startup: storage problems surface unchanged, like the rename store's own.
        store.Initialize();
        return Run(db => db.Users.AsNoTracking().Include(u => u.Logins).OrderBy(u => u.Id).AsEnumerable()
            .Select(ToRecord).ToArray());
    }

    public bool TryCreate(UserRecord user, out UserRecord created)
    {
        UserRecord? result = null;
        var ok = Run(db =>
        {
            var row = new UserRow
            {
                Username = user.Username,
                PasswordHash = user.PasswordHash,
                Role = user.Role.ToString(),
                Disabled = user.Disabled,
                Stamp = user.Stamp,
                CreatedAt = user.CreatedAt,
                LastSignInAt = user.LastSignInAt,
                Logins = user.Logins.Select(login => new UserLoginRow
                {
                    Issuer = login.Issuer,
                    Subject = login.Subject,
                    DisplayName = login.DisplayName,
                    LinkedAt = login.LinkedAt,
                    LastUsedAt = login.LastUsedAt
                }).ToList()
            };
            db.Users.Add(row);
            if (!TrySave(db))
                return false;
            result = ToRecord(row);
            return true;
        });
        created = result!;
        return ok;
    }

    public bool TryUpdate(UserRecord updated, string expectedStamp) => Run(db =>
        db.Users.Where(u => u.Id == updated.Id && u.Stamp == expectedStamp).ExecuteUpdate(setters => setters
            .SetProperty(u => u.Username, updated.Username)
            .SetProperty(u => u.PasswordHash, updated.PasswordHash)
            .SetProperty(u => u.Role, updated.Role.ToString())
            .SetProperty(u => u.Disabled, updated.Disabled)
            .SetProperty(u => u.Stamp, updated.Stamp)) == 1);

    public bool TryAddLogin(long userId, string expectedStamp, string newStamp, UserLoginRecord login) => Run(db =>
    {
        using var transaction = db.Database.BeginTransaction();
        if (!Restamp(db, userId, expectedStamp, newStamp))
            return false;
        db.UserLogins.Add(new UserLoginRow
        {
            UserId = userId,
            Issuer = login.Issuer,
            Subject = login.Subject,
            DisplayName = login.DisplayName,
            LinkedAt = login.LinkedAt
        });
        if (!TrySave(db))
            return false;
        transaction.Commit();
        return true;
    });

    public bool TryRemoveLogin(long userId, string expectedStamp, string newStamp, long loginId) => Run(db =>
    {
        using var transaction = db.Database.BeginTransaction();
        if (!Restamp(db, userId, expectedStamp, newStamp) ||
            db.UserLogins.Where(l => l.Id == loginId && l.UserId == userId).ExecuteDelete() != 1)
            return false;
        transaction.Commit();
        return true;
    });

    public bool TryMoveLogin(long loginId, long fromUserId, string fromStamp, long toUserId, string toStamp,
        string newStamp, bool deleteSource) => Run(db =>
    {
        using var transaction = db.Database.BeginTransaction();
        if (!Restamp(db, toUserId, toStamp, newStamp) ||
            db.UserLogins.Where(l => l.Id == loginId && l.UserId == fromUserId)
                .ExecuteUpdate(setters => setters.SetProperty(l => l.UserId, toUserId)) != 1)
            return false;
        // The source is restamped too (or removed), so its sessions end with the link.
        var source = deleteSource
            ? db.Users.Where(u => u.Id == fromUserId && u.Stamp == fromStamp).ExecuteDelete() == 1
            : Restamp(db, fromUserId, fromStamp, NewStamp());
        if (!source)
            return false;
        transaction.Commit();
        return true;
    });

    public bool TryDelete(long userId, string expectedStamp) => Run(db =>
        db.Users.Where(u => u.Id == userId && u.Stamp == expectedStamp).ExecuteDelete() == 1);

    public void RecordSignIn(long userId, long? loginId, DateTimeOffset at) => Run(db =>
    {
        db.Users.Where(u => u.Id == userId).ExecuteUpdate(setters => setters.SetProperty(u => u.LastSignInAt, at));
        if (loginId is { } id)
            db.UserLogins.Where(l => l.Id == id).ExecuteUpdate(setters => setters.SetProperty(l => l.LastUsedAt, at));
        return true;
    });

    public static string NewStamp() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    private static bool Restamp(RenameDbContext db, long userId, string expectedStamp, string newStamp)
        => db.Users.Where(u => u.Id == userId && u.Stamp == expectedStamp)
            .ExecuteUpdate(setters => setters.SetProperty(u => u.Stamp, newStamp)) == 1;

    private static bool TrySave(RenameDbContext db)
    {
        try
        {
            db.SaveChanges();
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException
        {
            SqliteExtendedErrorCode: UniqueViolation or PrimaryKeyViolation
        })
        {
            return false;
        }
    }

    private T Run<T>(Func<RenameDbContext, T> action)
    {
        if (!IsAvailable)
            throw new UserStorageException("Configure Namerre:DataPath to manage users.");
        try
        {
            store.Initialize();
            using var db = store.Open();
            return action(db);
        }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException or
            InvalidOperationException or Application.Renaming.RenameException)
        {
            throw new UserStorageException("Unable to save users to the database.", exception);
        }
    }

    private static UserRecord ToRecord(UserRow row) => new(row.Id, row.Username, row.PasswordHash,
        Enum.Parse<UserRole>(row.Role), row.Disabled, row.Stamp, row.CreatedAt, row.LastSignInAt,
        row.Logins.OrderBy(l => l.Id).Select(l => new UserLoginRecord(l.Id, l.UserId, l.Issuer, l.Subject,
            l.DisplayName, l.LinkedAt, l.LastUsedAt)).ToArray());
}
