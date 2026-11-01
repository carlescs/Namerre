using CMediator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Namerre.Application.Authentication;
using Namerre.Application.Users;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Namerre.Application.UnitTests.Authentication;

/// <summary>The account requests, sent through the mediator that <c>AddApplication()</c> registers.</summary>
public sealed class AccountRequestTests : IDisposable
{
    private const string Issuer = "https://keycloak.example/realms/media";
    private const string Password = "correct horse battery";
    private static readonly UserReference Someone = new(999, "someone");
    private readonly InMemoryUserStore store = new();
    private readonly IUserHistory history = Substitute.For<IUserHistory>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider provider;
    private readonly IServiceScope scope;

    public AccountRequestTests() : this(new TestAdministrator())
    {
    }

    private AccountRequestTests(IAdministratorConfiguration administrator)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IUserStore>(store);
        services.AddSingleton<IPasswordHasher>(TestPasswordHasher.Instance);
        services.AddSingleton(administrator);
        services.AddSingleton(history);
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        provider = services.BuildServiceProvider();
        scope = provider.CreateScope();
    }

    private ISender Sender => scope.ServiceProvider.GetRequiredService<ISender>();
    private UserAccounts Accounts => provider.GetRequiredService<UserAccounts>();

    [Fact]
    public async Task SetupReportsAnUnusableUsernameBeforeCheckingTheToken()
    {
        Assert.True(await Sender.Send(new RequiresSetupQuery(), CancellationToken.None));

        Assert.Equal(SetupResult.InvalidUsername,
            await Sender.Send(new CompleteSetupCommand(new string('0', 64), "  ", Password), CancellationToken.None));
        Assert.Equal(SetupResult.InvalidToken,
            await Sender.Send(new CompleteSetupCommand(new string('0', 64), "admin", Password), CancellationToken.None));
        Assert.Equal(SetupResult.Completed,
            await Sender.Send(new CompleteSetupCommand(Accounts.SetupToken!, "admin", Password), CancellationToken.None));

        Assert.False(await Sender.Send(new RequiresSetupQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task PasswordSignInRecordsOnlySuccessfulSignIns()
    {
        var alice = Accounts.Create(Someone, "alice", UserRole.User, Password, null);

        Assert.Null(await Sender.Send(new PasswordSignInCommand("alice", "wrong password"), CancellationToken.None));
        Assert.Null(Accounts.Find(alice.Id)!.LastSignInAt);

        var signedIn = await Sender.Send(new PasswordSignInCommand("alice", Password), CancellationToken.None);

        Assert.Equal(alice.Id, signedIn!.Id);
        Assert.Equal(time.GetUtcNow(), Accounts.Find(alice.Id)!.LastSignInAt);
    }

    [Fact]
    public async Task PasswordsAreVerifiedForKnownUsersOnly()
    {
        var alice = Accounts.Create(Someone, "alice", UserRole.User, Password, null);

        Assert.True(await Sender.Send(new VerifyPasswordQuery(alice.Id, Password), CancellationToken.None));
        Assert.False(await Sender.Send(new VerifyPasswordQuery(alice.Id, "wrong password"), CancellationToken.None));
        Assert.False(await Sender.Send(new VerifyPasswordQuery(alice.Id, null), CancellationToken.None));
        Assert.False(await Sender.Send(new VerifyPasswordQuery(alice.Id + 1, Password), CancellationToken.None));
    }

    [Fact]
    public async Task OwnChangesNeedARecentSignInOrThePassword()
    {
        var alice = Accounts.Create(Someone, "alice", UserRole.User, Password, null);

        Assert.Equal("RecentSignInRequired", (await Assert.ThrowsAsync<AccountException>(() => Sender.Send(
            new ConfirmOwnChangeCommand(alice.Id, false, "wrong password"), CancellationToken.None))).Code);
        Assert.Equal(alice.Id, (await Sender.Send(new ConfirmOwnChangeCommand(alice.Id, false, Password),
            CancellationToken.None)).Id);
        Assert.Equal(alice.Id, (await Sender.Send(new ConfirmOwnChangeCommand(alice.Id, true, null),
            CancellationToken.None)).Id);
        Assert.Equal("UserNotFound", (await Assert.ThrowsAsync<AccountException>(() => Sender.Send(
            new ConfirmOwnChangeCommand(alice.Id + 1, true, null), CancellationToken.None))).Code);
    }

    [Fact]
    public async Task TheConfiguredAdministratorIsManagedInConfigurationOnly()
    {
        using var configured = new AccountRequestTests(new TestAdministrator("root",
            TestPasswordHasher.Instance.Hash(Password)));
        var root = new UserReference(UserAccounts.ConfiguredUserId, "root");

        Assert.Equal("ManagedByConfiguration", (await Assert.ThrowsAsync<AccountException>(() => configured.Sender
            .Send(new ConfirmOwnChangeCommand(root.Id, true, null), CancellationToken.None))).Code);
        // Reported as such even when the new password would also be refused.
        Assert.Equal("ManagedByConfiguration", (await Assert.ThrowsAsync<AccountException>(() => configured.Sender
            .Send(new SetPasswordCommand(root, root.Id, Password, "short"), CancellationToken.None))).Code);
        Assert.Equal("root", (await configured.Sender.Send(new GetUserQuery(root.Id), CancellationToken.None))!
            .Username);
    }

    [Fact]
    public async Task RemovingTheOwnPasswordNeedsARecentSignIn()
    {
        var alice = Accounts.Create(Someone, "alice", UserRole.User, Password, new(Issuer, "sub-1", "alice"));
        var self = new UserReference(alice.Id, alice.Username);

        Assert.Equal("RecentSignInRequired", (await Assert.ThrowsAsync<AccountException>(() => Sender.Send(
            new RemoveOwnPasswordCommand(self, false), CancellationToken.None))).Code);
        Assert.True(Accounts.Find(alice.Id)!.HasPassword);

        Assert.False((await Sender.Send(new RemoveOwnPasswordCommand(self, true), CancellationToken.None))
            .HasPassword);
    }

    [Fact]
    public async Task AdministratorsManageUsersThroughTheMediator()
    {
        var alice = await Sender.Send(new CreateUserCommand(Someone, "alice", UserRole.User, Password, null),
            CancellationToken.None);
        var bob = await Sender.Send(new CreateUserCommand(Someone, "bob", UserRole.Admin, null,
            new(Issuer, "sub-1", "bob")), CancellationToken.None);

        alice = await Sender.Send(new UpdateUserCommand(Someone, alice.Id, UserRole.Admin, null),
            CancellationToken.None);
        alice = await Sender.Send(new LinkLoginCommand(Someone, alice.Id, new(Issuer, "sub-2", "alice")),
            CancellationToken.None);
        alice = await Sender.Send(new RemovePasswordCommand(Someone, alice.Id), CancellationToken.None);
        alice = await Sender.Send(new SetPasswordCommand(Someone, alice.Id, null, "another long password"),
            CancellationToken.None);
        alice = await Sender.Send(new UnlinkLoginCommand(Someone, alice.Id, alice.Logins[0].Id),
            CancellationToken.None);
        alice = await Sender.Send(new MoveLoginCommand(Someone, bob.Logins[0].Id, alice.Id, true),
            CancellationToken.None);

        Assert.Equal((UserRole.Admin, true, "sub-1"), (alice.Role, alice.HasPassword, Assert.Single(alice.Logins).Subject));
        Assert.Equal([alice.Id], (await Sender.Send(new ListUsersQuery(), CancellationToken.None)).Select(u => u.Id));
        // Alice is the last administrator now.
        Assert.Equal("LastAdmin", (await Assert.ThrowsAsync<AccountException>(() => Sender.Send(
            new DeleteUserCommand(Someone, alice.Id), CancellationToken.None))).Code);
        var carol = Accounts.Create(Someone, "carol", UserRole.User, Password, null);
        Assert.Equal("carol", (await Sender.Send(new DeleteUserCommand(Someone, carol.Id), CancellationToken.None))
            .Username);
        Assert.Null(await Sender.Send(new GetUserQuery(carol.Id), CancellationToken.None));
    }

    [Fact]
    public async Task MergingAttributesTheSourceHistoryToTheTarget()
    {
        var alice = Accounts.Create(Someone, "alice", UserRole.Admin, Password, null);
        var provisioned = Accounts.Create(Someone, "alice-2", UserRole.User, null, new(Issuer, "sub-1", "alice"));

        var merged = await Sender.Send(new MergeUsersCommand(Someone, provisioned.Id, alice.Id),
            CancellationToken.None);

        Assert.Equal("sub-1", Assert.Single(merged.Logins).Subject);
        await history.Received(1).Reattribute(provisioned.Id, new UserReference(alice.Id, "alice"),
            CancellationToken.None);
    }

    [Fact]
    public async Task AHistoryFailureAfterMergingIsReportedButKeepsTheMerge()
    {
        var alice = Accounts.Create(Someone, "alice", UserRole.Admin, Password, null);
        var provisioned = Accounts.Create(Someone, "alice-2", UserRole.User, null, new(Issuer, "sub-1", "alice"));
        history.Reattribute(default, default!, default).ReturnsForAnyArgs(Task.FromException<int>(new IOException()));

        var error = await Assert.ThrowsAsync<AccountException>(() => Sender.Send(
            new MergeUsersCommand(Someone, provisioned.Id, alice.Id), CancellationToken.None));

        Assert.Equal(("HistoryNotMerged", 503), (error.Code, error.Status));
        Assert.Null(Accounts.Find(provisioned.Id));
        Assert.Single(Accounts.Find(alice.Id)!.Logins);
    }

    [Fact]
    public async Task MergingAnUnknownUserIsNotFoundAndCancellationIsNotAHistoryFailure()
    {
        var alice = Accounts.Create(Someone, "alice", UserRole.Admin, Password, null);
        var provisioned = Accounts.Create(Someone, "alice-2", UserRole.User, null, new(Issuer, "sub-1", "alice"));

        Assert.Equal("UserNotFound", (await Assert.ThrowsAsync<AccountException>(() => Sender.Send(
            new MergeUsersCommand(Someone, provisioned.Id + 1, alice.Id), CancellationToken.None))).Code);

        history.Reattribute(default, default!, default).ThrowsAsyncForAnyArgs(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Sender.Send(
            new MergeUsersCommand(Someone, provisioned.Id, alice.Id), CancellationToken.None));
    }

    public void Dispose()
    {
        scope.Dispose();
        provider.Dispose();
    }
}
