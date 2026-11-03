using Namerre.Application.Authentication;
using Namerre.Authentication;
using Xunit;

namespace Namerre.Api.UnitTests.Authentication;

public sealed class UserSessionTests
{
    private static readonly UserRecord User = new(7, "alice", null, UserRole.User, false, "stamp-1",
        DateTimeOffset.UnixEpoch, null, [], []);

    [Fact]
    public void EverySessionGetsItsOwnRandomIdAndTheEpochItWasIssuedWith()
    {
        var now = DateTimeOffset.UtcNow;

        var first = UserSession.Create(User, UserSession.PasswordMethod, null, now, 3);
        var second = UserSession.Create(User, UserSession.PasswordMethod, null, now, 3);

        Assert.Equal(48, UserSession.SessionId(first)!.Length);
        Assert.NotEqual(UserSession.SessionId(first), UserSession.SessionId(second));
        Assert.Equal(3, UserSession.SessionEpoch(first));
    }

    [Fact]
    public void RenewingKeepsTheSessionIdAndEpochUnlessTheSessionSurvivedEndingTheOthers()
    {
        var current = UserSession.Create(User, UserSession.PasswordMethod, null, DateTimeOffset.UtcNow, 2);
        var changed = User with { Stamp = "stamp-2" };

        var renewed = UserSession.Renew(current, changed);
        var kept = UserSession.Renew(current, User, epoch: 5);

        Assert.Equal(UserSession.SessionId(current), UserSession.SessionId(renewed));
        Assert.Equal(2, UserSession.SessionEpoch(renewed));
        Assert.Equal(UserSession.SessionId(current), UserSession.SessionId(kept));
        Assert.Equal(5, UserSession.SessionEpoch(kept));
    }

    [Fact]
    public void ASessionWithoutAnEpochClaimCountsAsEpochZero()
        => Assert.Equal(0, UserSession.SessionEpoch(new System.Security.Claims.ClaimsPrincipal()));
}
