using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Authentication;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Namerre.Application.UnitTests.Authentication;

public sealed class PasskeyAccountsTests
{
    private const string Password = "correct horse battery";
    private const string Answer = "{\"browser\":\"answer\"}";
    private static readonly PasskeyRelyingParty RelyingParty = new("namerre.example", "https://namerre.example");
    private static readonly PasskeyChallenge Challenge = new("{}", "state");
    private readonly InMemoryUserStore store = new();
    private readonly IPasskeyCeremonies ceremonies = Substitute.For<IPasskeyCeremonies>();
    private readonly UserAccounts accounts;
    private readonly PasskeyAccounts passkeys;
    private readonly UserRecord alice;

    public PasskeyAccountsTests()
    {
        accounts = new UserAccounts(new TestAdministrator(null, null), store, TestPasswordHasher.Instance,
            NullLogger<UserAccounts>.Instance);
        passkeys = new PasskeyAccounts(accounts, ceremonies, NullLogger<PasskeyAccounts>.Instance);
        alice = accounts.Create(new UserReference(0, "admin"), "alice", UserRole.User, Password, null);
        ceremonies.BeginRegistration(default!, default!, default!).ReturnsForAnyArgs(Challenge);
        ceremonies.BeginAssertion(default!).ReturnsForAnyArgs(Challenge);
    }

    [Fact]
    public void RegisteringNeedsARecentSignInOrThePassword()
    {
        Assert.Equal("RecentSignInRequired", Assert.Throws<AccountException>(() =>
            passkeys.BeginRegistration(alice.Id, false, null, RelyingParty)).Code);
        Assert.Equal("RecentSignInRequired", Assert.Throws<AccountException>(() =>
            passkeys.BeginRegistration(alice.Id, false, "wrong password", RelyingParty)).Code);

        Assert.Same(Challenge, passkeys.BeginRegistration(alice.Id, false, Password, RelyingParty).Challenge);
        Assert.Same(Challenge, passkeys.BeginRegistration(alice.Id, true, null, RelyingParty).Challenge);
    }

    [Fact]
    public void TheConfiguredAdministratorCannotRegisterPasskeys()
    {
        var configured = new UserAccounts(new TestAdministrator("admin", TestPasswordHasher.Instance.Hash(Password)),
            new InMemoryUserStore(), TestPasswordHasher.Instance, NullLogger<UserAccounts>.Instance);

        Assert.Equal("ManagedByConfiguration", Assert.Throws<AccountException>(() =>
            new PasskeyAccounts(configured, ceremonies, NullLogger<PasskeyAccounts>.Instance)
                .BeginRegistration(UserAccounts.ConfiguredUserId, true, null, RelyingParty)).Code);
    }

    [Fact]
    public async Task RegisteringAddsASignInMethodAndEndsOtherSessions()
    {
        var registration = passkeys.BeginRegistration(alice.Id, true, null, RelyingParty);
        // Every passkey of a user shares one random user handle, never derived from the user.
        ceremonies.Received().BeginRegistration(RelyingParty, Arg.Is<UserRecord>(u => u.Id == alice.Id),
            Arg.Is<byte[]>(handle => handle.Length == 32));

        var updated = await Register(registration.User.Stamp, [1, 2, 3], " Laptop\n");

        var passkey = Assert.Single(updated.Passkeys);
        Assert.Equal(("Laptop", alice.Id), (passkey.Name, passkey.UserId));
        Assert.Equal(2, updated.SignInMethods);
        Assert.NotEqual(alice.Stamp, updated.Stamp);

        var second = passkeys.BeginRegistration(alice.Id, true, null, RelyingParty);
        ceremonies.Received().BeginRegistration(RelyingParty, Arg.Any<UserRecord>(),
            Arg.Is<byte[]>(handle => handle.SequenceEqual(passkey.UserHandle)));
        Assert.Equal("Passkey 2", (await Register(second.User.Stamp, [4], "")).Passkeys[1].Name);
    }

    [Fact]
    public async Task RegistrationIsPinnedToTheUserVersionThatStartedIt()
    {
        var registration = passkeys.BeginRegistration(alice.Id, true, null, RelyingParty);
        accounts.SetPassword(new UserReference(alice.Id, "alice"), alice.Id, Password, "another long password", false);

        Assert.Equal("UserChanged", (await Assert.ThrowsAsync<AccountException>(() =>
            Register(registration.User.Stamp, [1]))).Code);
        Assert.Empty(accounts.Find(alice.Id)!.Passkeys);
    }

    [Fact]
    public async Task APasskeyBelongsToOneUser()
    {
        await Register(alice.Stamp, [1, 2, 3]);
        var bob = accounts.Create(new UserReference(0, "admin"), "bob", UserRole.User, Password, null);

        Assert.Equal("PasskeyTaken", (await Assert.ThrowsAsync<AccountException>(() =>
            Register(bob.Stamp, [1, 2, 3], user: bob))).Code);
    }

    [Fact]
    public async Task FailedVerificationIsReportedWithoutDetails()
    {
        ceremonies.CompleteRegistration("state", Answer, Arg.Any<CancellationToken>())
            .ThrowsAsync(new PasskeyVerificationException("origin mismatch"));

        var exception = await Assert.ThrowsAsync<AccountException>(() => passkeys.CompleteRegistration(
            new UserReference(alice.Id, "alice"), alice.Stamp, "state", Answer, null, CancellationToken.None));

        Assert.Equal(("PasskeyRejected", 400), (exception.Code, exception.Status));
        Assert.DoesNotContain("origin", exception.Message);
    }

    [Fact]
    public async Task UsersHaveAtMostTwentyPasskeys()
    {
        var stamp = alice.Stamp;
        for (byte i = 0; i < UserAccounts.MaximumPasskeysPerUser; i++)
            stamp = (await Register(stamp, [i])).Stamp;

        Assert.Equal("TooManyPasskeys", Assert.Throws<AccountException>(() =>
            passkeys.BeginRegistration(alice.Id, true, null, RelyingParty)).Code);
        Assert.Equal("TooManyPasskeys", (await Assert.ThrowsAsync<AccountException>(() =>
            Register(stamp, [99]))).Code);
    }

    [Fact]
    public async Task SigningInReturnsTheOwnerAndRecordsTheCounter()
    {
        await Register(alice.Stamp, [1, 2, 3]);
        ceremonies.AssertedCredentialId(Answer).Returns([1, 2, 3]);
        ceremonies.CompleteAssertion("state", Answer, Arg.Any<UserPasskeyRecord>(), Arg.Any<CancellationToken>())
            .Returns(7u);

        var user = await passkeys.SignIn("state", Answer, CancellationToken.None);

        Assert.Equal(alice.Id, user?.Id);
        var stored = accounts.Find(alice.Id)!;
        var passkey = Assert.Single(stored.Passkeys);
        Assert.Equal(7u, passkey.SignCount);
        Assert.NotNull(passkey.LastUsedAt);
        Assert.NotNull(stored.LastSignInAt);
    }

    [Fact]
    public async Task UnknownPasskeysDisabledUsersAndFailedVerificationDoNotSignIn()
    {
        await Register(alice.Stamp, [1, 2, 3]);
        ceremonies.AssertedCredentialId(Answer).Returns([9]);
        Assert.Null(await passkeys.SignIn("state", Answer, CancellationToken.None));

        ceremonies.AssertedCredentialId(Answer).Returns([1, 2, 3]);
        ceremonies.CompleteAssertion("state", Answer, Arg.Any<UserPasskeyRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PasskeyVerificationException("bad signature"));
        Assert.Null(await passkeys.SignIn("state", Answer, CancellationToken.None));

        ceremonies.CompleteAssertion("state", Answer, Arg.Any<UserPasskeyRecord>(), Arg.Any<CancellationToken>())
            .Returns(1u);
        accounts.Update(new UserReference(0, "admin"), alice.Id, null, true);
        Assert.Null(await passkeys.SignIn("state", Answer, CancellationToken.None));
    }

    [Fact]
    public async Task ASignInRacingAnotherUseOfThePasskeyFailsAndKeepsTheNewerCounter()
    {
        await Register(alice.Stamp, [1, 2, 3]);
        ceremonies.AssertedCredentialId(Answer).Returns([1, 2, 3]);
        // Another sign-in with the same passkey records counter 12 while this one is verified against 0.
        ceremonies.CompleteAssertion("state", Answer, Arg.Any<UserPasskeyRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                store.Tamper(alice.Id, u => u with { Passkeys = [u.Passkeys[0] with { SignCount = 12 }] });
                return 11u;
            });

        Assert.Null(await passkeys.SignIn("state", Answer, CancellationToken.None));
        Assert.Equal(12u, store.List().Single(u => u.Id == alice.Id).Passkeys[0].SignCount);
    }

    [Fact]
    public async Task APasskeyRemovedWhileSigningInDoesNotSignIn()
    {
        await Register(alice.Stamp, [1, 2, 3]);
        ceremonies.AssertedCredentialId(Answer).Returns([1, 2, 3]);
        // An administrator removes the passkey after the service checked it, before the sign-in is recorded.
        ceremonies.CompleteAssertion("state", Answer, Arg.Any<UserPasskeyRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                store.Tamper(alice.Id, u => u with { Passkeys = [], Stamp = "removed" });
                return 1u;
            });

        Assert.Null(await passkeys.SignIn("state", Answer, CancellationToken.None));
    }

    [Fact]
    public async Task ASignInIsRefusedWhenItsCounterCannotBeSaved()
    {
        await Register(alice.Stamp, [1, 2, 3]);
        ceremonies.AssertedCredentialId(Answer).Returns([1, 2, 3]);
        ceremonies.CompleteAssertion("state", Answer, Arg.Any<UserPasskeyRecord>(), Arg.Any<CancellationToken>())
            .Returns(1u);
        store.Failure = new UserStorageException("read-only");

        await Assert.ThrowsAsync<UserStorageException>(() => passkeys.SignIn("state", Answer, CancellationToken.None));
    }

    [Fact]
    public async Task MergingKeepsThePasskeyLimit()
    {
        var admin = accounts.Create(new UserReference(0, "setup"), "root", UserRole.Admin, Password, null);
        var stamp = alice.Stamp;
        for (byte i = 0; i < UserAccounts.MaximumPasskeysPerUser; i++)
            stamp = (await Register(stamp, [i])).Stamp;
        var rootStamp = (await Register(admin.Stamp, [200], user: admin)).Stamp;

        Assert.Equal("TooManyPasskeys", Assert.Throws<AccountException>(() =>
            accounts.Merge(new UserReference(admin.Id, "root"), alice.Id, admin.Id)).Code);
        Assert.Equal(rootStamp, accounts.Find(admin.Id)!.Stamp);
        Assert.Equal(UserAccounts.MaximumPasskeysPerUser, accounts.Find(alice.Id)!.Passkeys.Count);
    }

    [Fact]
    public async Task APasskeyCountsAsASignInMethodButNeverTheLast()
    {
        var actor = new UserReference(alice.Id, "alice");
        var withPasskey = await Register(alice.Stamp, [1]);

        var passwordless = accounts.RemovePassword(actor, alice.Id);
        Assert.False(passwordless.HasPassword);
        Assert.Equal("LastSignInMethod", Assert.Throws<AccountException>(() =>
            accounts.RemovePasskey(actor, alice.Id, withPasskey.Passkeys[0].Id)).Code);

        var withPassword = accounts.SetPassword(actor, alice.Id, null, "another long password", true);
        var removed = accounts.RemovePasskey(actor, alice.Id, withPasskey.Passkeys[0].Id);
        Assert.Empty(removed.Passkeys);
        Assert.NotEqual(withPassword.Stamp, removed.Stamp);
        Assert.Equal("PasskeyNotFound", Assert.Throws<AccountException>(() =>
            accounts.RemovePasskey(actor, alice.Id, withPasskey.Passkeys[0].Id)).Code);
    }

    [Fact]
    public async Task RenamingAPasskeyKeepsSessions()
    {
        var actor = new UserReference(alice.Id, "alice");
        var registered = await Register(alice.Stamp, [1], "  ");
        var passkey = Assert.Single(registered.Passkeys);
        Assert.Equal("Passkey 1", passkey.Name);

        var renamed = accounts.RenamePasskey(actor, alice.Id, passkey.Id, " Phone\u0007 " + new string('x', 200));
        var name = Assert.Single(renamed.Passkeys).Name;
        Assert.StartsWith("Phone x", name);
        Assert.Equal(UserAccounts.MaximumPasskeyNameLength, name.Length);
        // A label is not a sign-in method: the credential stamp, and with it every session, stays.
        Assert.Equal(registered.Stamp, renamed.Stamp);
        Assert.Equal(name, accounts.Find(alice.Id)!.Passkeys[0].Name);

        Assert.Equal("PasskeyNameRequired", Assert.Throws<AccountException>(() =>
            accounts.RenamePasskey(actor, alice.Id, passkey.Id, " \t ")).Code);
        Assert.Equal("PasskeyNotFound", Assert.Throws<AccountException>(() =>
            accounts.RenamePasskey(actor, alice.Id, passkey.Id + 1, "Other")).Code);
        var bob = accounts.Create(new UserReference(0, "admin"), "bob", UserRole.User, Password, null);
        Assert.Equal("PasskeyNotFound", Assert.Throws<AccountException>(() =>
            accounts.RenamePasskey(actor, bob.Id, passkey.Id, "Other")).Code);
    }

    [Fact]
    public async Task MergingMovesPasskeys()
    {
        var admin = accounts.Create(new UserReference(0, "setup"), "root", UserRole.Admin, Password, null);
        await Register(alice.Stamp, [1, 2, 3]);

        var merged = accounts.Merge(new UserReference(admin.Id, "root"), alice.Id, admin.Id);

        Assert.Equal([1, 2, 3], Assert.Single(merged.Passkeys).CredentialId);
        Assert.Equal(admin.Id, accounts.FindPasskey([1, 2, 3])?.User.Id);
    }

    private Task<UserRecord> Register(string stamp, byte[] credentialId, string? name = null, UserRecord? user = null)
    {
        user ??= alice;
        ceremonies.CompleteRegistration("state", Answer, Arg.Any<CancellationToken>())
            .Returns(new NewPasskey(credentialId, [42], [5, 5], 0));
        return passkeys.CompleteRegistration(new UserReference(user.Id, user.Username), stamp, "state", Answer, name,
            CancellationToken.None);
    }
}
