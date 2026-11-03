using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Authentication;
using Namerre.Infrastructure.Renaming;
using Xunit;

namespace Namerre.Infrastructure.UnitTests.Authentication;

public sealed class SessionRegistryTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "namerre-infra-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualClock time = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly List<RenameStore> stores = [];

    public SessionRegistryTests() => Directory.CreateDirectory(Path.Combine(workspace, "root"));

    public void Dispose()
    {
        foreach (var store in stores)
            store.Dispose();
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }

    /// <summary>A registry over the workspace database, as a freshly started process would open it.</summary>
    private SessionRegistry Open(bool configured = true)
    {
        foreach (var previous in stores)
            previous.Dispose();
        stores.Clear();
        var store = new RenameStore(new BoundedFileSystem("root", workspace), configured ? "data" : "", workspace);
        stores.Add(store);
        return new SessionRegistry(store, NullLogger<SessionRegistry>.Instance, time);
    }

    [Fact]
    public void RevokedSessionsAndEpochsSurviveARestart()
    {
        var registry = Open();
        Assert.False(registry.IsRevoked("abc"));
        Assert.Equal(0, registry.Epoch(0));

        registry.Revoke("abc");
        Assert.Equal(1, registry.AdvanceEpoch(0));
        Assert.Equal(2, registry.AdvanceEpoch(0));
        Assert.Equal(1, registry.AdvanceEpoch(5));
        Assert.True(registry.IsRevoked("abc"));

        var restarted = Open();
        Assert.True(restarted.IsRevoked("abc"));
        Assert.False(restarted.IsRevoked("other"));
        Assert.Equal(2, restarted.Epoch(0));
        Assert.Equal(1, restarted.Epoch(5));
        Assert.Equal(0, restarted.Epoch(9));
        Assert.Equal(3, restarted.AdvanceEpoch(0));
    }

    [Fact]
    public void RevokingTwiceIsHarmless()
    {
        var registry = Open();

        registry.Revoke("abc");
        registry.Revoke("abc");

        Assert.True(Open().IsRevoked("abc"));
    }

    [Fact]
    public void RevokedSessionsAreForgottenOnceTheirCookieWouldHaveExpired()
    {
        var registry = Open();
        registry.Revoke("old");
        time.Advance(SessionPolicy.Lifetime - TimeSpan.FromMinutes(1));
        Assert.True(Open().IsRevoked("old"));

        time.Advance(TimeSpan.FromMinutes(2));
        registry = Open();
        Assert.False(registry.IsRevoked("old"));

        // Pruning also happens when another session is revoked, in memory and in the database.
        registry.Revoke("new");
        Assert.True(Open().IsRevoked("new"));
    }

    [Fact]
    public void PruningOnRevokeRemovesExpiredRows()
    {
        var registry = Open();
        registry.Revoke("old");
        time.Advance(SessionPolicy.Lifetime + TimeSpan.FromMinutes(1));

        registry.Revoke("new");

        Assert.False(registry.IsRevoked("old"));
        Assert.True(registry.IsRevoked("new"));
        using var db = stores[0].Open();
        Assert.Equal(["new"], db.RevokedSessions.Select(r => r.SessionId).ToArray());
    }

    [Fact]
    public void WithoutADataDirectoryRevocationsLastForTheProcess()
    {
        var registry = Open(configured: false);

        registry.Revoke("abc");
        Assert.Equal(1, registry.AdvanceEpoch(0));

        Assert.True(registry.IsRevoked("abc"));
        Assert.Equal(1, registry.Epoch(0));
        Assert.False(Open(configured: false).IsRevoked("abc"));
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
