using Namerre.Application.Authentication;

namespace Namerre.Api.UnitTests.Authentication;

/// <summary>A stateful <see cref="IUserStore"/> with the same uniqueness and compare-and-swap rules as the database.</summary>
internal sealed class InMemoryUserStore : IUserStore
{
    private readonly List<UserRecord> users = [];
    private long nextUser = 1;
    private long nextLogin = 1;

    public bool IsAvailable { get; set; } = true;
    /// <summary>Thrown by every write when set, to simulate storage failures.</summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<UserRecord> List() => IsAvailable ? users.ToArray() : [];

    public bool TryCreate(UserRecord user, out UserRecord created)
    {
        Write();
        created = null!;
        if (users.Any(u => string.Equals(u.Username, user.Username, StringComparison.OrdinalIgnoreCase)) ||
            user.Logins.Any(l => LoginTaken(l.Issuer, l.Subject)))
            return false;
        var id = nextUser++;
        created = user with
        {
            Id = id,
            Logins = user.Logins.Select(l => l with { Id = nextLogin++, UserId = id }).ToArray()
        };
        users.Add(created);
        return true;
    }

    public bool TryUpdate(UserRecord updated, string expectedStamp)
    {
        Write();
        var index = users.FindIndex(u => u.Id == updated.Id && u.Stamp == expectedStamp);
        if (index < 0)
            return false;
        users[index] = updated with { Logins = users[index].Logins, CreatedAt = users[index].CreatedAt };
        return true;
    }

    public bool TryAddLogin(long userId, string expectedStamp, string newStamp, UserLoginRecord login)
    {
        Write();
        var index = users.FindIndex(u => u.Id == userId && u.Stamp == expectedStamp);
        if (index < 0 || LoginTaken(login.Issuer, login.Subject))
            return false;
        users[index] = users[index] with
        {
            Stamp = newStamp,
            Logins = [.. users[index].Logins, login with { Id = nextLogin++, UserId = userId }]
        };
        return true;
    }

    public bool TryRemoveLogin(long userId, string expectedStamp, string newStamp, long loginId)
    {
        Write();
        var index = users.FindIndex(u => u.Id == userId && u.Stamp == expectedStamp);
        if (index < 0 || users[index].Logins.All(l => l.Id != loginId))
            return false;
        users[index] = users[index] with
        {
            Stamp = newStamp,
            Logins = users[index].Logins.Where(l => l.Id != loginId).ToArray()
        };
        return true;
    }

    public bool TryMoveLogin(long loginId, long fromUserId, string fromStamp, long toUserId, string toStamp,
        string newStamp, bool deleteSource)
    {
        Write();
        var from = users.FindIndex(u => u.Id == fromUserId && u.Stamp == fromStamp);
        var to = users.FindIndex(u => u.Id == toUserId && u.Stamp == toStamp);
        if (from < 0 || to < 0)
            return false;
        var login = users[from].Logins.FirstOrDefault(l => l.Id == loginId);
        if (login is null)
            return false;
        users[to] = users[to] with { Stamp = newStamp, Logins = [.. users[to].Logins, login with { UserId = toUserId }] };
        if (deleteSource)
            users.RemoveAt(from);
        else
            users[from] = users[from] with
            {
                Stamp = Guid.NewGuid().ToString("N"),
                Logins = users[from].Logins.Where(l => l.Id != loginId).ToArray()
            };
        return true;
    }

    public bool TryDelete(long userId, string expectedStamp)
    {
        Write();
        return users.RemoveAll(u => u.Id == userId && u.Stamp == expectedStamp) == 1;
    }

    public void RecordSignIn(long userId, long? loginId, DateTimeOffset at)
    {
        Write();
        var index = users.FindIndex(u => u.Id == userId);
        if (index >= 0)
            users[index] = users[index] with
            {
                LastSignInAt = at,
                Logins = users[index].Logins.Select(l => l.Id == loginId ? l with { LastUsedAt = at } : l).ToArray()
            };
    }

    /// <summary>Changes a stored user behind the service's back, e.g. to simulate a concurrent edit.</summary>
    public void Tamper(long userId, Func<UserRecord, UserRecord> change)
    {
        var index = users.FindIndex(u => u.Id == userId);
        users[index] = change(users[index]);
    }

    private bool LoginTaken(string issuer, string subject)
        => users.Any(u => u.Logins.Any(l => l.Issuer == issuer && l.Subject == subject));

    private void Write()
    {
        if (Failure is not null)
            throw Failure;
    }
}
