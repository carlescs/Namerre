using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Authentication;
using Xunit;

namespace Namerre.Application.UnitTests.Authentication;

public sealed class UserAccountsTests
{
    private const string Issuer = "https://keycloak.example/realms/media";
    private const string Password = "correct horse battery";
    private static readonly string Hash = TestPasswordHasher.Instance.Hash(Password);
    private static readonly UserReference Someone = new(999, "someone");
    private readonly InMemoryUserStore store = new();

    [Fact]
    public void ConfiguredCredentialsAreABreakGlassAdministrator()
    {
        var accounts = Accounts("admin", Hash);

        Assert.False(accounts.RequiresSetup);
        Assert.Null(accounts.SetupToken);
        var admin = Assert.Single(accounts.List());
        Assert.Equal((UserAccounts.ConfiguredUserId, "admin", UserRole.Admin), (admin.Id, admin.Username, admin.Role));
        Assert.Same(admin, accounts.Authenticate("admin", Password));
        Assert.Equal("ManagedByConfiguration", Assert.Throws<AccountException>(() =>
            accounts.SetPassword(Someone, UserAccounts.ConfiguredUserId, null, "another long password", true)).Code);
        Assert.Equal("ManagedByConfiguration", Assert.Throws<AccountException>(() =>
            accounts.Delete(Someone, UserAccounts.ConfiguredUserId)).Code);
    }

    [Theory]
    [InlineData("admin", "")]
    [InlineData("", "valid")]
    [InlineData("admin", "not a hash")]
    public void InvalidConfiguredCredentialsFailClosedAtStartup(string username, string hash)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Accounts(username, hash == "valid" ? Hash : hash));

        Assert.Contains("Namerre:Authentication", exception.Message);
    }

    [Fact]
    public void OverlongConfiguredUsernamesAreRejected()
        => Assert.Throws<InvalidOperationException>(() => Accounts(new string('a', 129), Hash));

    [Fact]
    public void SetupCreatesTheFirstAdministratorOnceWithTheConsoleToken()
    {
        var accounts = Accounts();
        Assert.True(accounts.RequiresSetup);
        var token = accounts.SetupToken!;

        Assert.Equal(SetupResult.InvalidToken, accounts.CompleteSetup(new string('0', 64), "first", Password));
        Assert.Equal(SetupResult.WeakPassword, accounts.CompleteSetup(token, "first", "short"));
        Assert.Equal(SetupResult.Completed, accounts.CompleteSetup(token, "first", Password));
        Assert.Equal(SetupResult.AlreadyConfigured, accounts.CompleteSetup(token, "second", Password));

        var user = Assert.Single(accounts.List());
        Assert.Equal(("first", UserRole.Admin), (user.Username, user.Role));
        Assert.Null(accounts.SetupToken);
        Assert.False(Accounts().RequiresSetup);
    }

    [Theory]
    [InlineData("alice", Password, true)]
    [InlineData("alice", "wrong", false)]
    [InlineData("Alice", Password, false)]
    [InlineData("bob", Password, false)]
    public void AuthenticationRequiresTheExactUsernameAndPassword(string username, string password, bool expected)
    {
        var accounts = Accounts();
        accounts.Create(Someone, "alice", UserRole.User, Password, null);

        Assert.Equal(expected, accounts.Authenticate(username, password) is not null);
    }

    [Fact]
    public void DisabledUsersAndUsersWithoutAPasswordCannotSignInWithOne()
    {
        var accounts = Accounts("admin", Hash);
        var alice = accounts.Create(Someone, "alice", UserRole.User, Password, null);
        accounts.Create(Someone, "sso", UserRole.User, null, new(Issuer, "sub-1", "sso"));

        accounts.Update(Someone, alice.Id, null, disabled: true);

        Assert.Null(accounts.Authenticate("alice", Password));
        Assert.Null(accounts.Authenticate("sso", ""));
        Assert.Null(accounts.Authenticate("sso", Password));
    }

    [Fact]
    public void UsernamesAreUniqueIgnoringCaseAndMustBeUsable()
    {
        var accounts = Accounts("admin", Hash);
        accounts.Create(Someone, "alice", UserRole.User, Password, null);

        Assert.Equal("UsernameTaken", Assert.Throws<AccountException>(() =>
            accounts.Create(Someone, "ALICE", UserRole.User, Password, null)).Code);
        Assert.Equal("UsernameTaken", Assert.Throws<AccountException>(() =>
            accounts.Create(Someone, "Admin", UserRole.User, Password, null)).Code);
        Assert.Equal("InvalidUsername", Assert.Throws<AccountException>(() =>
            accounts.Create(Someone, "a\tb", UserRole.User, Password, null)).Code);
        Assert.Equal("WeakPassword", Assert.Throws<AccountException>(() =>
            accounts.Create(Someone, "bob", UserRole.User, "too short", null)).Code);
        Assert.Equal("LastSignInMethod", Assert.Throws<AccountException>(() =>
            accounts.Create(Someone, "bob", UserRole.User, null, null)).Code);
    }

    [Fact]
    public void TheLastActiveAdministratorCannotBeDemotedDisabledOrDeleted()
    {
        var accounts = Accounts();
        var admin = accounts.Create(Someone, "admin", UserRole.Admin, Password, null);
        var other = new UserReference(admin.Id + 100, "other");

        Assert.Equal("LastAdmin", Assert.Throws<AccountException>(() =>
            accounts.Update(other, admin.Id, UserRole.User, null)).Code);
        Assert.Equal("LastAdmin", Assert.Throws<AccountException>(() =>
            accounts.Update(other, admin.Id, null, true)).Code);
        Assert.Equal("LastAdmin", Assert.Throws<AccountException>(() => accounts.Delete(other, admin.Id)).Code);

        var second = accounts.Create(Someone, "second", UserRole.Admin, Password, null);
        accounts.Update(other, admin.Id, UserRole.User, null);

        Assert.Equal(UserRole.User, accounts.Find(admin.Id)!.Role);
        Assert.Equal("LastAdmin", Assert.Throws<AccountException>(() => accounts.Delete(other, second.Id)).Code);
    }

    [Fact]
    public void TheConfiguredAdministratorCountsAsAnActiveAdministrator()
    {
        var accounts = Accounts("root", Hash);
        var admin = accounts.Create(Someone, "admin", UserRole.Admin, Password, null);

        accounts.Delete(Someone, admin.Id);

        Assert.Null(accounts.Find(admin.Id));
    }

    [Fact]
    public void AdministratorsCannotDemoteDisableOrDeleteThemselves()
    {
        var accounts = Accounts("root", Hash);
        var admin = accounts.Create(Someone, "admin", UserRole.Admin, Password, null);
        var self = new UserReference(admin.Id, admin.Username);

        Assert.Equal("OwnAccount", Assert.Throws<AccountException>(() =>
            accounts.Update(self, admin.Id, UserRole.User, null)).Code);
        Assert.Equal("OwnAccount", Assert.Throws<AccountException>(() =>
            accounts.Update(self, admin.Id, null, true)).Code);
        Assert.Equal("OwnAccount", Assert.Throws<AccountException>(() => accounts.Delete(self, admin.Id)).Code);
    }

    [Fact]
    public void EveryAccessChangeReplacesTheStamp()
    {
        var accounts = Accounts("root", Hash);
        var user = accounts.Create(Someone, "alice", UserRole.User, Password, null);
        var stamps = new List<string> { user.Stamp };

        stamps.Add(accounts.Update(Someone, user.Id, UserRole.Admin, null).Stamp);
        stamps.Add(accounts.SetPassword(Someone, user.Id, null, "another long password", false).Stamp);
        stamps.Add(accounts.Link(Someone, user.Id, new(Issuer, "sub-1", "alice")).Stamp);
        stamps.Add(accounts.RemovePassword(Someone, user.Id).Stamp);
        var withPassword = accounts.SetPassword(Someone, user.Id, null, "yet another password", false);
        stamps.Add(withPassword.Stamp);
        stamps.Add(accounts.Unlink(Someone, user.Id, withPassword.Logins[0].Id).Stamp);
        stamps.Add(accounts.Update(Someone, user.Id, null, true).Stamp);

        Assert.Equal(stamps.Count, stamps.Distinct().Count());
    }

    [Fact]
    public void UsersChangeTheirPasswordWithTheCurrentOne()
    {
        var accounts = Accounts("root", Hash);
        var user = accounts.Create(Someone, "alice", UserRole.User, Password, null);
        var self = new UserReference(user.Id, "alice");

        Assert.Equal("InvalidPassword", Assert.Throws<AccountException>(() =>
            accounts.SetPassword(self, user.Id, "wrong", "another long password", true)).Code);
        Assert.Equal("InvalidPassword", Assert.Throws<AccountException>(() =>
            accounts.SetPassword(self, user.Id, null, "another long password", true)).Code);
        Assert.Equal("WeakPassword", Assert.Throws<AccountException>(() =>
            accounts.SetPassword(self, user.Id, Password, "short", true)).Code);

        accounts.SetPassword(self, user.Id, Password, "another long password", false);

        Assert.NotNull(accounts.Authenticate("alice", "another long password"));
        Assert.Null(accounts.Authenticate("alice", Password));
    }

    [Fact]
    public void SingleSignOnUsersSetAFirstPasswordAfterARecentSignIn()
    {
        var accounts = Accounts("root", Hash);
        var (user, _, _) = accounts.SignInExternal(new(Issuer, "sub-1", "alice"), autoProvision: true);
        var self = new UserReference(user!.Id, user.Username);

        Assert.Equal("RecentSignInRequired", Assert.Throws<AccountException>(() =>
            accounts.SetPassword(self, user.Id, null, "a brand new password", recentSignIn: false)).Code);

        var updated = accounts.SetPassword(self, user.Id, null, "a brand new password", recentSignIn: true);

        Assert.True(updated.HasPassword);
        Assert.Equal(2, updated.SignInMethods);
        Assert.NotNull(accounts.Authenticate("alice", "a brand new password"));
    }

    [Fact]
    public void TheLastSignInMethodOfAnEnabledUserCannotBeRemoved()
    {
        var accounts = Accounts("root", Hash);
        var local = accounts.Create(Someone, "local", UserRole.User, Password, null);
        var sso = accounts.Create(Someone, "sso", UserRole.User, null, new(Issuer, "sub-1", "sso"));

        Assert.Equal("LastSignInMethod", Assert.Throws<AccountException>(() =>
            accounts.RemovePassword(Someone, local.Id)).Code);
        Assert.Equal("LastSignInMethod", Assert.Throws<AccountException>(() =>
            accounts.Unlink(Someone, sso.Id, sso.Logins[0].Id)).Code);

        // A disabled user may be left without one, but cannot be enabled again until it has one.
        accounts.Update(Someone, local.Id, null, true);
        accounts.RemovePassword(Someone, local.Id);

        Assert.Equal("LastSignInMethod", Assert.Throws<AccountException>(() =>
            accounts.Update(Someone, local.Id, null, false)).Code);
    }

    [Fact]
    public void FirstSingleSignOnCreatesAUserWithTheUserRoleAndKeepsUsingIt()
    {
        var accounts = Accounts("root", Hash);

        var (first, firstLogin, _) = accounts.SignInExternal(new(Issuer, "sub-1", "alice"), true);
        accounts.Update(Someone, first!.Id, UserRole.Admin, null);
        var (again, againLogin, _) = accounts.SignInExternal(new(Issuer, "sub-1", "alice-renamed"), true);

        Assert.Equal(("alice", UserRole.User), (first.Username, first.Role));
        Assert.Equal(first.Id, again!.Id);
        Assert.Equal(firstLogin, againLogin);
        // The role is managed in Namerre, so a later sign-in keeps the promotion.
        Assert.Equal(UserRole.Admin, again.Role);
        Assert.Single(accounts.List(), u => u.Id != UserAccounts.ConfiguredUserId);
    }

    [Fact]
    public void ProvisioningNeverLinksAnExistingUserWithTheSameUsername()
    {
        var accounts = Accounts("root", Hash);
        var alice = accounts.Create(Someone, "alice", UserRole.Admin, Password, null);

        var (provisioned, _, _) = accounts.SignInExternal(new(Issuer, "sub-1", "Alice"), true);

        Assert.NotEqual(alice.Id, provisioned!.Id);
        Assert.Equal(("Alice-2", UserRole.User), (provisioned.Username, provisioned.Role));
        Assert.Empty(accounts.Find(alice.Id)!.Logins);
    }

    [Fact]
    public void WithoutProvisioningUnlinkedIdentitiesAreRefused()
    {
        var accounts = Accounts("root", Hash);
        accounts.Create(Someone, "alice", UserRole.User, Password, null);

        var result = accounts.SignInExternal(new(Issuer, "sub-1", "alice"), autoProvision: false);

        Assert.Equal((null, null, ExternalSignInFailure.NotLinked), result);
        Assert.Equal(2, accounts.List().Count);
    }

    [Fact]
    public void ProvisioningWaitsUntilTheFirstAdministratorIsInstalled()
    {
        var accounts = Accounts();

        Assert.Equal(ExternalSignInFailure.SetupRequired,
            accounts.SignInExternal(new(Issuer, "sub-1", "alice"), autoProvision: true).Failure);
        Assert.True(accounts.RequiresSetup);
        Assert.Equal(SetupResult.Completed, accounts.CompleteSetup(accounts.SetupToken!, "admin", Password));
        Assert.Equal(ExternalSignInFailure.None,
            accounts.SignInExternal(new(Issuer, "sub-1", "alice"), autoProvision: true).Failure);
    }

    [Fact]
    public void DisabledUsersCannotSignInWithSingleSignOn()
    {
        var accounts = Accounts("root", Hash);
        var (user, _, _) = accounts.SignInExternal(new(Issuer, "sub-1", "alice"), true);
        accounts.Update(Someone, user!.Id, null, true);

        Assert.Equal(ExternalSignInFailure.Disabled,
            accounts.SignInExternal(new(Issuer, "sub-1", "alice"), true).Failure);
    }

    [Fact]
    public void AnIdentityLinkedToAnotherUserIsRefused()
    {
        var accounts = Accounts("root", Hash);
        var alice = accounts.Create(Someone, "alice", UserRole.User, Password, new(Issuer, "sub-1", "alice"));
        var bob = accounts.Create(Someone, "bob", UserRole.User, Password, null);

        Assert.Equal("LoginTaken", Assert.Throws<AccountException>(() =>
            accounts.Link(Someone, bob.Id, new(Issuer, "sub-1", "alice"))).Code);
        Assert.Equal("LoginTaken", Assert.Throws<AccountException>(() =>
            accounts.Create(Someone, "carol", UserRole.User, null, new(Issuer, "sub-1", "alice"))).Code);
        Assert.Equal("AlreadyLinked", Assert.Throws<AccountException>(() =>
            accounts.Link(Someone, alice.Id, new(Issuer, "sub-1", "alice"))).Code);
        // The same subject from another issuer is a different identity.
        Assert.Single(accounts.Link(Someone, bob.Id, new("https://other.example", "sub-1", "bob")).Logins);
    }

    [Fact]
    public void LinkingWithAStaleStampIsRefused()
    {
        var accounts = Accounts("root", Hash);
        var alice = accounts.Create(Someone, "alice", UserRole.User, Password, null);
        accounts.SetPassword(Someone, alice.Id, null, "another long password", false);

        Assert.Equal("UserChanged", Assert.Throws<AccountException>(() =>
            accounts.Link(Someone, alice.Id, new(Issuer, "sub-1", "alice"), alice.Stamp)).Code);
    }

    [Fact]
    public void MovingALinkFromAProvisionedUserDeletesItWhenAsked()
    {
        var accounts = Accounts("root", Hash);
        var alice = accounts.Create(Someone, "alice", UserRole.Admin, Password, null);
        var (provisioned, loginId, _) = accounts.SignInExternal(new(Issuer, "sub-1", "alice"), true);

        Assert.Equal("LastSignInMethod", Assert.Throws<AccountException>(() =>
            accounts.MoveLogin(Someone, loginId!.Value, alice.Id, deleteSource: false)).Code);

        var moved = accounts.MoveLogin(Someone, loginId!.Value, alice.Id, deleteSource: true);

        Assert.Equal("sub-1", Assert.Single(moved.Logins).Subject);
        Assert.Null(accounts.Find(provisioned!.Id));
        Assert.Equal(alice.Id, accounts.SignInExternal(new(Issuer, "sub-1", "alice"), true).User!.Id);
    }

    [Fact]
    public void MergingMovesEveryIdentityToTheTargetAndDeletesTheSource()
    {
        var accounts = Accounts("root", Hash);
        var alice = accounts.Create(Someone, "alice", UserRole.Admin, Password, null);
        var provisioned = accounts.Create(Someone, "alice-2", UserRole.User, "another long password",
            new(Issuer, "sub-1", "alice"));
        provisioned = accounts.Link(Someone, provisioned.Id, new("https://other.example", "sub-9", "alice"));

        var merged = accounts.Merge(Someone, provisioned.Id, alice.Id);

        Assert.Equal(["sub-1", "sub-9"], merged.Logins.Select(l => l.Subject));
        Assert.Equal((UserRole.Admin, true), (merged.Role, merged.HasPassword));
        Assert.NotEqual(alice.Stamp, merged.Stamp);
        Assert.Null(accounts.Find(provisioned.Id));
        Assert.Equal(alice.Id, accounts.Authenticate("alice", Password)!.Id);
        // The source's password goes with it.
        Assert.Null(accounts.Authenticate("alice-2", "another long password")); // betterleaks:allow
        Assert.Equal(alice.Id, accounts.SignInExternal(new(Issuer, "sub-1", "alice"), true).User!.Id);
    }

    [Fact]
    public void MergingFollowsTheDeletionRules()
    {
        var accounts = Accounts();
        var admin = accounts.Create(Someone, "admin", UserRole.Admin, Password, null);
        var user = accounts.Create(Someone, "user", UserRole.User, Password, null);
        var self = new UserReference(admin.Id, "admin");

        Assert.Equal("OwnAccount", Assert.Throws<AccountException>(() => accounts.Merge(self, admin.Id, user.Id)).Code);
        Assert.Equal("LastAdmin", Assert.Throws<AccountException>(() =>
            accounts.Merge(Someone, admin.Id, user.Id)).Code);
        Assert.Equal("SameUser", Assert.Throws<AccountException>(() =>
            accounts.Merge(Someone, user.Id, user.Id)).Code);
        Assert.Equal("ManagedByConfiguration", Assert.Throws<AccountException>(() =>
            Accounts("root", Hash).Merge(Someone, UserAccounts.ConfiguredUserId, user.Id)).Code);
    }

    [Fact]
    public void ConcurrentChangesAreDetected()
    {
        var accounts = Accounts("root", Hash);
        var alice = accounts.Create(Someone, "alice", UserRole.User, Password, null);
        store.Tamper(alice.Id, user => user with { Stamp = "changed elsewhere" });

        Assert.Equal("UserChanged", Assert.Throws<AccountException>(() =>
            accounts.Update(Someone, alice.Id, UserRole.Admin, null)).Code);
        Assert.Equal("changed elsewhere", accounts.Find(alice.Id)!.Stamp);
    }

    [Fact]
    public void StorageFailuresLeaveUsersUnchanged()
    {
        var accounts = Accounts("root", Hash);
        var alice = accounts.Create(Someone, "alice", UserRole.User, Password, null);
        store.Failure = new UserStorageException("db");

        Assert.Throws<UserStorageException>(() => accounts.Update(Someone, alice.Id, UserRole.Admin, null));
        Assert.Equal(ExternalSignInFailure.Unavailable,
            accounts.SignInExternal(new(Issuer, "sub-1", "bob"), true).Failure);
        Assert.Equal(alice, accounts.Find(alice.Id));
    }

    private UserAccounts Accounts(string? username = null, string? hash = null)
        => new(new TestAdministrator(username, hash), store, TestPasswordHasher.Instance,
            NullLogger<UserAccounts>.Instance);
}
