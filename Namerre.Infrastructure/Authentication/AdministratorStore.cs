using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Renaming;

namespace Namerre.Infrastructure.Authentication;

public sealed class AdministratorStore(RenameStore store) : IAdministratorStore
{
    public AdminCredentials? Load()
    {
        store.Initialize();
        using var db = store.Open();
        var account = db.Administrators.AsNoTracking().SingleOrDefault();
        return account is null ? null : new AdminCredentials(account.Username, account.PasswordHash);
    }

    public bool TryCreate(AdminCredentials credentials)
    {
        store.Initialize();
        using var db = store.Open();
        db.Administrators.Add(new AdministratorAccount
        {
            Id = 1,
            Username = credentials.Username,
            PasswordHash = credentials.PasswordHash
        });
        try
        {
            db.SaveChanges();
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 })
        {
            return false;
        }
        catch (DbUpdateException exception)
        {
            throw new AdministratorStorageException("Unable to save the administrator to the database.", exception);
        }
    }

    public bool TryUpdate(AdminCredentials current, string passwordHash)
    {
        store.Initialize();
        using var db = store.Open();
        try
        {
            // Compare-and-swap: only replace the hash this process last loaded.
            return db.Administrators
                .Where(a => a.Id == 1 && a.Username == current.Username && a.PasswordHash == current.PasswordHash)
                .ExecuteUpdate(setters => setters.SetProperty(a => a.PasswordHash, passwordHash)) == 1;
        }
        catch (SqliteException exception)
        {
            throw new AdministratorStorageException("Unable to save the administrator to the database.", exception);
        }
    }
}
