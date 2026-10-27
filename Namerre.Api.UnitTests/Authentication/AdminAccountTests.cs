using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Authentication;
using Namerre.Infrastructure.Configuration;
using NSubstitute;
using Xunit;

namespace Namerre.Api.UnitTests.Authentication;

public sealed class AdminAccountTests
{
    // Hashing is deliberately slow, so share one hash across tests.
    private static readonly string Hash = AdminAccount.HashPassword("correct horse");
    private readonly IAdministratorStore store = Substitute.For<IAdministratorStore>();

    [Fact]
    public void ConfiguredCredentialsTakePrecedenceOverTheStore()
    {
        var account = new AdminAccount(Configured("admin", Hash), store);

        Assert.False(account.RequiresSetup);
        Assert.Equal("admin", account.Username);
        Assert.Null(account.SetupToken);
        store.DidNotReceive().Load();
    }

    [Fact]
    public void StoredCredentialsAreUsedWhenNothingIsConfigured()
    {
        store.Load().Returns(new AdminCredentials("stored", Hash));

        var account = new AdminAccount(Configured(null, null), store);

        Assert.False(account.RequiresSetup);
        Assert.Equal("stored", account.Username);
    }

    [Theory]
    [InlineData("admin", "")]
    [InlineData("", "valid")]
    [InlineData("admin", "not base64!")]
    [InlineData("admin", "AQIDBA==")]
    public void InvalidCredentialsFailClosedAtStartup(string username, string hash)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AdminAccount(Configured(username, hash == "valid" ? Hash : hash), store));

        Assert.Contains("Namerre:Authentication", exception.Message);
    }

    [Fact]
    public void OverlongUsernamesAreRejected()
        => Assert.Throws<InvalidOperationException>(() =>
            new AdminAccount(Configured(new string('a', 129), Hash), store));

    [Fact]
    public void WeakerHashWorkFactorsAreRejected()
    {
        var bytes = Convert.FromBase64String(Hash);
        bytes[5] = 0; bytes[6] = 0; bytes[7] = 0x27; bytes[8] = 0x10; // 10,000 iterations

        Assert.Throws<InvalidOperationException>(() =>
            new AdminAccount(Configured("admin", Convert.ToBase64String(bytes)), store));
    }

    [Theory]
    [InlineData("admin", "correct horse", true)]
    [InlineData("admin", "wrong", false)]
    [InlineData("Admin", "correct horse", false)]
    [InlineData("other", "correct horse", false)]
    public void VerifyRequiresExactUsernameAndPassword(string username, string password, bool expected)
    {
        var account = new AdminAccount(Configured("admin", Hash), store);

        Assert.Equal(expected, account.Verify(username, password));
    }

    [Fact]
    public void AuthenticateReturnsTheVerifiedCredentialsSnapshot()
    {
        store.Load().Returns(new AdminCredentials("stored", Hash));
        store.TryUpdate(Arg.Any<AdminCredentials>(), Arg.Any<string>()).Returns(true);
        var account = new AdminAccount(Configured(null, null), store);

        var verified = account.Authenticate("stored", "correct horse");
        account.ChangePassword("correct horse", "battery staple", out _);

        // A login verified before the change keeps the old, now revoked, stamp.
        Assert.Equal(new AdminCredentials("stored", Hash), verified);
        Assert.NotEqual(account.Stamp, AdminAccount.StampOf(verified));
        Assert.Null(account.Authenticate("stored", "correct horse"));
    }

    [Fact]
    public void StampChangesWithTheCredentials()
    {
        var first = new AdminAccount(Configured("admin", Hash), store);
        var second = new AdminAccount(Configured("other", Hash), store);

        Assert.Matches("^[0-9A-F]{64}$", first.Stamp);
        Assert.Equal(first.Stamp, new AdminAccount(Configured("admin", Hash), store).Stamp);
        Assert.NotEqual(first.Stamp, second.Stamp);
    }

    [Fact]
    public void UnconfiguredAccountRequiresSetupWithAOneTimeToken()
    {
        var account = new AdminAccount(Configured(null, null), store);

        Assert.True(account.RequiresSetup);
        Assert.Equal("", account.Username);
        Assert.Equal("", account.Stamp);
        Assert.Matches("^[0-9A-F]{64}$", account.SetupToken);
        Assert.False(account.Verify("", ""));
    }

    [Fact]
    public void SetupWithTheTokenStoresAHashedAdministrator()
    {
        store.TryCreate(Arg.Any<AdminCredentials>()).Returns(true);
        var account = new AdminAccount(Configured(null, null), store);

        var result = account.CompleteSetup(account.SetupToken!, "admin", "s3cret passphrase");

        Assert.Equal(SetupResult.Completed, result);
        Assert.False(account.RequiresSetup);
        Assert.Null(account.SetupToken);
        Assert.True(account.Verify("admin", "s3cret passphrase"));
        store.Received(1).TryCreate(Arg.Is<AdminCredentials>(c =>
            c.Username == "admin" && c.PasswordHash != "s3cret passphrase"));
        Assert.Equal(SetupResult.AlreadyConfigured, account.CompleteSetup("token", "again", "pass"));
    }

    [Fact]
    public void SetupRejectsWrongTokens()
    {
        var account = new AdminAccount(Configured(null, null), store);

        Assert.Equal(SetupResult.InvalidToken, account.CompleteSetup(new string('0', 64), "admin", "pass"));
        Assert.Equal(SetupResult.InvalidToken, account.CompleteSetup("", "admin", "pass"));
        Assert.True(account.RequiresSetup);
        store.DidNotReceive().TryCreate(Arg.Any<AdminCredentials>());
    }

    [Fact]
    public void SetupReportsConfiguredWhenAnotherInstanceWonTheRace()
    {
        store.TryCreate(Arg.Any<AdminCredentials>()).Returns(false);
        var account = new AdminAccount(Configured(null, null), store);

        Assert.Equal(SetupResult.AlreadyConfigured,
            account.CompleteSetup(account.SetupToken!, "admin", "s3cret passphrase"));
        Assert.True(account.RequiresSetup);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("eleven char")]
    [InlineData("            ")]
    public void SetupRejectsPasswordsShorterThanTheMinimum(string password)
    {
        var account = new AdminAccount(Configured(null, null), store);
        var token = account.SetupToken!;

        Assert.Equal(SetupResult.WeakPassword, account.CompleteSetup(token, "admin", password));

        // The token stays valid so the administrator can retry with a longer password.
        Assert.True(account.RequiresSetup);
        Assert.Equal(token, account.SetupToken);
        store.DidNotReceive().TryCreate(Arg.Any<AdminCredentials>());
    }

    [Fact]
    public void SetupIsRefusedOnceConfigured()
        => Assert.Equal(SetupResult.AlreadyConfigured,
            new AdminAccount(Configured("admin", Hash), store).CompleteSetup("token", "admin", "pass"));

    [Fact]
    public void StoredAdministratorCanChangeThePassword()
    {
        var stored = new AdminCredentials("stored", Hash);
        store.Load().Returns(stored);
        store.TryUpdate(stored, Arg.Any<string>()).Returns(true);
        var account = new AdminAccount(Configured(null, null), store);
        var stamp = account.Stamp;

        Assert.True(account.CanChangePassword);
        Assert.Equal(PasswordChangeResult.Changed,
            account.ChangePassword("correct horse", "battery staple", out var changed));

        Assert.Equal(account.Stamp, AdminAccount.StampOf(changed));
        Assert.True(account.Verify("stored", "battery staple"));
        Assert.False(account.Verify("stored", "correct horse"));
        Assert.NotEqual(stamp, account.Stamp);
        store.Received(1).TryUpdate(stored, Arg.Is<string>(hash => hash != "battery staple"));
    }

    [Fact]
    public void PasswordChangeRequiresTheCurrentPassword()
    {
        store.Load().Returns(new AdminCredentials("stored", Hash));
        var account = new AdminAccount(Configured(null, null), store);

        Assert.Equal(PasswordChangeResult.InvalidPassword, account.ChangePassword("wrong", "battery staple", out _));

        Assert.True(account.Verify("stored", "correct horse"));
        store.DidNotReceiveWithAnyArgs().TryUpdate(default!, default!);
    }

    [Fact]
    public void PasswordChangeRejectsNewPasswordsShorterThanTheMinimum()
    {
        store.Load().Returns(new AdminCredentials("stored", Hash));
        var account = new AdminAccount(Configured(null, null), store);

        Assert.Equal(PasswordChangeResult.WeakPassword, account.ChangePassword("correct horse", "short", out var changed));

        Assert.Null(changed);
        Assert.True(account.Verify("stored", "correct horse"));
        store.DidNotReceiveWithAnyArgs().TryUpdate(default!, default!);
    }

    [Fact]
    public void ExistingShorterPasswordsStillSignIn()
    {
        var shortHash = AdminAccount.HashPassword("short");
        var account = new AdminAccount(Configured("admin", shortHash), store);

        Assert.True(account.Verify("admin", "short"));
    }

    [Theory]
    [InlineData("a-twelve-chr", true)]
    [InlineData("eleven char", false)]
    [InlineData("             ", false)]
    public void PasswordsNeedAtLeastTwelveCharacters(string password, bool expected)
        => Assert.Equal(expected, AdminAccount.IsAcceptablePassword(password));

    [Fact]
    public void PasswordChangeKeepsTheOldPasswordWhenTheStoredAccountChanged()
    {
        store.Load().Returns(new AdminCredentials("stored", Hash));
        store.TryUpdate(Arg.Any<AdminCredentials>(), Arg.Any<string>()).Returns(false);
        var account = new AdminAccount(Configured(null, null), store);

        Assert.Equal(PasswordChangeResult.Conflict, account.ChangePassword("correct horse", "battery staple", out _));

        Assert.True(account.Verify("stored", "correct horse"));
    }

    [Fact]
    public void ConfiguredCredentialsCannotBeChanged()
    {
        var account = new AdminAccount(Configured("admin", Hash), store);

        Assert.False(account.CanChangePassword);
        Assert.Equal(PasswordChangeResult.ManagedByConfiguration,
            account.ChangePassword("correct horse", "battery staple", out _));
        Assert.True(account.Verify("admin", "correct horse"));
        store.DidNotReceiveWithAnyArgs().TryUpdate(default!, default!);
    }

    [Fact]
    public void UnconfiguredAccountCannotChangeAPassword()
    {
        var account = new AdminAccount(Configured(null, null), store);

        Assert.False(account.CanChangePassword);
        Assert.Equal(PasswordChangeResult.ManagedByConfiguration, account.ChangePassword("", "new", out _));
    }

    private static IOptions<NamerreOptions> Configured(string? username, string? hash) => Options.Create(
        new NamerreOptions { Authentication = new AuthenticationOptions { Username = username, PasswordHash = hash } });
}
