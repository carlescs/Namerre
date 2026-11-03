using CMediator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Authentication;
using Namerre.Testing;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Authentication;

public sealed class UserSessionsTests
{
    private static readonly UserReference Alice = new(7, "alice");
    private readonly ISessionRegistry registry = Substitute.For<ISessionRegistry>();

    private UserSessions Sessions() => new(registry, NullLogger<UserSessions>.Instance);

    [Fact]
    public void ASessionIsActiveUntilItIsRevokedOrItsEpochFallsBehind()
    {
        registry.Epoch(7).Returns(2L);
        registry.IsRevoked("ended").Returns(true);
        var sessions = Sessions();

        Assert.True(sessions.IsActive("live", 7, 2));
        Assert.True(sessions.IsActive("live", 7, 3));
        Assert.False(sessions.IsActive("live", 7, 1));
        Assert.False(sessions.IsActive("ended", 7, 2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ASessionWithoutAnIdIsNeverActive(string? sessionId)
    {
        Assert.False(Sessions().IsActive(sessionId, 7, 0));
        registry.DidNotReceiveWithAnyArgs().IsRevoked(default!);
    }

    [Fact]
    public void EndingASessionRevokesItAndIgnoresAnEmptyId()
    {
        var sessions = Sessions();

        sessions.End(Alice, "abc");
        sessions.End(Alice, "");

        registry.Received(1).Revoke("abc");
        registry.DidNotReceive().Revoke("");
    }

    [Fact]
    public void EndingAllSessionsAdvancesTheEpochOfThatUserOnly()
    {
        registry.AdvanceEpoch(7).Returns(5L);

        Assert.Equal(5L, Sessions().EndAll(Alice));

        registry.Received(1).AdvanceEpoch(7);
        registry.DidNotReceive().AdvanceEpoch(Arg.Is<long>(id => id != 7));
    }

    [Fact]
    public void TheConfiguredAdministratorHasItsOwnEpochToo()
    {
        var sessions = new UserSessions(new InMemorySessionRegistry(), NullLogger<UserSessions>.Instance);
        var admin = new UserReference(UserAccounts.ConfiguredUserId, "admin");
        var before = sessions.CurrentEpoch(admin.Id);

        var after = sessions.EndAll(admin);

        Assert.True(sessions.IsActive("kept", admin.Id, after));
        Assert.False(sessions.IsActive("older", admin.Id, before));
        Assert.True(sessions.IsActive("another-user", 8, 0));
    }

    [Fact]
    public async Task TheRequestsReachTheRegistryThroughTheMediator()
    {
        var provider = new ServiceCollection()
            .AddApplication()
            .AddSingleton(registry)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        registry.Epoch(7).Returns(3L);
        registry.AdvanceEpoch(7).Returns(4L);

        Assert.Equal(3L, await sender.Send(new SessionEpochQuery(7), CancellationToken.None));
        Assert.True(await sender.Send(new EndSessionCommand(Alice, "abc"), CancellationToken.None));
        Assert.Equal(4L, await sender.Send(new EndOtherSessionsCommand(Alice), CancellationToken.None));

        registry.Received(1).Revoke("abc");
        registry.Received(1).AdvanceEpoch(7);
    }
}
