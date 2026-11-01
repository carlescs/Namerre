using System.Security.Claims;
using CMediator.Abstractions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Namerre.Api.UnitTests.Authentication;
using Namerre.Application;
using Namerre.Application.Authentication;
using Namerre.Application.Renaming;
using Namerre.Authentication;
using Namerre.Controllers;
using Namerre.Infrastructure.Authentication;
using Namerre.Infrastructure.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using AuthenticationOptions = Namerre.Infrastructure.Configuration.AuthenticationOptions;

namespace Namerre.Api.UnitTests.Controllers;

public sealed class AuthControllerTests
{
    private static readonly PasswordHasher Hasher = new();
    private static readonly string Hash = Hasher.Hash("password");
    private readonly InMemoryUserStore store = new();
    private readonly ManualTime time = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly IAntiforgery antiforgery = Substitute.For<IAntiforgery>();
    private readonly IAuthenticationService authentication = Substitute.For<IAuthenticationService>();
    private readonly IConfigurationManager<OpenIdConnectConfiguration> providerMetadata =
        Substitute.For<IConfigurationManager<OpenIdConnectConfiguration>>();

    [Fact]
    public async Task SessionIssuesXsrfCookieAndReportsSetupState()
    {
        antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet("request-token", "cookie-token", "form", "X-XSRF-TOKEN"));
        var controller = Controller(Unconfigured());

        var result = Assert.IsType<OkObjectResult>(await controller.Session(CancellationToken.None));

        var cookie = controller.Response.Headers.SetCookie.ToString();
        Assert.Contains("XSRF-TOKEN=request-token", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.DoesNotContain("httponly", cookie);
        Assert.Equivalent(new { username = (string?)null, requiresSetup = true, canChangePassword = false },
            result.Value);
    }

    [Fact]
    public async Task SessionReportsTheSignedInAdministrator()
    {
        antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet("t", "c", "form", "X-XSRF-TOKEN"));
        var account = Configured();
        var controller = Controller(account);
        SignIn(controller, account.Find(UserAccounts.ConfiguredUserId)!);

        var result = Assert.IsType<OkObjectResult>(await controller.Session(CancellationToken.None));

        Assert.Equivalent(new
        {
            username = "admin", userId = 0L, role = "Admin", requiresSetup = false, canChangePassword = false,
            managedByConfiguration = true, recentSignIn = true
        }, result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetupRequiresAUsername(string username)
    {
        var account = Unconfigured();

        var result = Assert.IsType<BadRequestObjectResult>(await Controller(account).Setup(
            new SetupRequest(account.SetupToken!, username, "a long enough password"), CancellationToken.None));

        Assert.Equivalent(new { message = "Enter an administrator username." }, result.Value);
        Assert.True(account.RequiresSetup);
    }

    [Fact]
    public async Task SetupWithInvalidTokenIsForbidden()
    {
        var result = await Controller(Unconfigured()).Setup(
            new SetupRequest(new string('0', 64), "admin", "password"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task SetupAfterConfigurationConflicts()
        => Assert.IsType<ConflictObjectResult>(await Controller(Configured())
            .Setup(new SetupRequest(new string('0', 64), "admin", "password"), CancellationToken.None));

    [Fact]
    public async Task SuccessfulSetupReturnsNoContent()
    {
        var account = Unconfigured();

        Assert.IsType<NoContentResult>(await Controller(account).Setup(
            new SetupRequest(account.SetupToken!, "admin", "a long enough password"), CancellationToken.None));
        Assert.False(account.RequiresSetup);
    }

    [Fact]
    public async Task SetupWithAShortPasswordIsABadRequest()
    {
        var account = Unconfigured();

        var result = Assert.IsType<BadRequestObjectResult>(await Controller(account).Setup(
            new SetupRequest(account.SetupToken!, "admin", "password"), CancellationToken.None));

        Assert.Equivalent(new { message = "Use a password of at least 12 characters." }, result.Value);
        Assert.True(account.RequiresSetup);
    }

    public static TheoryData<Exception> StorageFailures => new()
    {
        new IOException("disk"),
        new UnauthorizedAccessException("denied"),
        new RenameException("DataUnavailable", "db"),
        new UserStorageException("db", new IOException())
    };

    [Theory]
    [MemberData(nameof(StorageFailures))]
    public async Task StorageFailuresDuringSetupAreServiceUnavailable(Exception failure)
    {
        var account = Unconfigured();
        store.Failure = failure;

        var result = await Controller(account).Setup(
            new SetupRequest(account.SetupToken!, "admin", "a long enough password"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.True(account.RequiresSetup);
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("other", "password")]
    public async Task LoginWithInvalidCredentialsIsUnauthorized(string username, string password)
    {
        var result = await Controller(Configured()).Login(new LoginRequest(username, password), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await authentication.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task LoginSignsInWithACredentialStamp()
    {
        var account = Configured();
        var controller = Controller(account);

        Assert.IsType<NoContentResult>(await controller.Login(new LoginRequest("admin", "password"), CancellationToken.None));

        await authentication.Received(1).SignInAsync(controller.HttpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Is<ClaimsPrincipal>(p => p.Identity!.Name == "admin"
                && p.FindFirst(ClaimTypes.NameIdentifier)!.Value == "0"
                && p.FindFirst(ClaimTypes.Role)!.Value == "Admin"
                && p.FindFirst("credential-stamp")!.Value == account.Find(0)!.Stamp),
            Arg.Is<AuthenticationProperties>(p => !p.IsPersistent));
    }

    [Fact]
    public async Task LogoutSignsOut()
    {
        var controller = Controller(Configured());

        Assert.IsType<NoContentResult>(await controller.Logout(CancellationToken.None));

        await authentication.Received(1).SignOutAsync(controller.HttpContext,
            CookieAuthenticationDefaults.AuthenticationScheme, Arg.Any<AuthenticationProperties?>());
    }

    [Fact]
    public async Task ChangingThePasswordKeepsTheCurrentSessionWithTheNewStamp()
    {
        var (account, user) = Stored();
        var controller = Controller(account);
        SignIn(controller, user);

        Assert.IsType<NoContentResult>(await controller.ChangePassword(
            new ChangePasswordRequest("password", "new password"), CancellationToken.None));

        var changed = account.Find(user.Id)!;
        Assert.NotNull(account.Authenticate("alice", "new password"));
        Assert.NotEqual(user.Stamp, changed.Stamp);
        await authentication.Received(1).SignInAsync(controller.HttpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Is<ClaimsPrincipal>(p => p.Identity!.Name == "alice"
                && p.FindFirst("credential-stamp")!.Value == changed.Stamp),
            Arg.Is<AuthenticationProperties>(p => !p.IsPersistent));
    }

    [Fact]
    public async Task ChangingThePasswordWithAWrongCurrentPasswordIsForbidden()
    {
        var (account, user) = Stored();
        var controller = Controller(account);
        SignIn(controller, user);

        var error = await Assert.ThrowsAsync<AccountException>(() =>
            controller.ChangePassword(
            new ChangePasswordRequest("wrong", "new password"), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, error.Status);
        Assert.NotNull(account.Authenticate("alice", "password"));
        await authentication.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task ChangingToAShortPasswordIsABadRequest()
    {
        var (account, user) = Stored();
        var controller = Controller(account);
        SignIn(controller, user);

        var error = await Assert.ThrowsAsync<AccountException>(() =>
            controller.ChangePassword(
            new ChangePasswordRequest("password", "too short"), CancellationToken.None));

        Assert.Equal((StatusCodes.Status400BadRequest, "Use a password of at least 12 characters."),
            (error.Status, error.Message));
        Assert.NotNull(account.Authenticate("alice", "password"));
    }

    [Fact]
    public async Task ConfiguredPasswordsCannotBeChangedThroughTheApi()
    {
        var account = Configured();
        var controller = Controller(account);
        SignIn(controller, account.Find(UserAccounts.ConfiguredUserId)!);

        var error = await Assert.ThrowsAsync<AccountException>(() =>
            controller.ChangePassword(
            new ChangePasswordRequest("password", "new password"), CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, error.Status);
    }

    [Fact]
    public async Task StorageFailuresDuringPasswordChangeLeaveThePasswordUnchanged()
    {
        var (account, user) = Stored();
        var controller = Controller(account);
        SignIn(controller, user);
        store.Failure = new UserStorageException("db");

        await Assert.ThrowsAsync<UserStorageException>(() =>
            controller.ChangePassword(
            new ChangePasswordRequest("password", "new password"), CancellationToken.None));

        store.Failure = null;
        Assert.NotNull(account.Authenticate("alice", "password"));
    }

    [Fact]
    public async Task SingleSignOnUsersSetAFirstPasswordOnlyAfterARecentSignIn()
    {
        var oidc = Oidc();
        var account = Configured();
        var (user, _, _) = account.SignInExternal(new ExternalIdentity(oidc.Issuer, "sub-1", "alice"), true);
        var controller = Controller(account, oidc);
        SignIn(controller, user!, UserSession.SingleSignOnMethod, oidc.Stamp);
        time.Advance(TimeSpan.FromMinutes(11));

        var stale = await Assert.ThrowsAsync<AccountException>(() =>
            controller.ChangePassword(
            new ChangePasswordRequest(null, "a brand new password"), CancellationToken.None));
        SignIn(controller, user!, UserSession.SingleSignOnMethod, oidc.Stamp);
        Assert.IsType<NoContentResult>(await controller.ChangePassword(
            new ChangePasswordRequest(null, "a brand new password"), CancellationToken.None));

        Assert.Equal("RecentSignInRequired", stale.Code);
        Assert.NotNull(account.Authenticate("alice", "a brand new password"));
        // The renewed session is still a single sign-on session.
        await authentication.Received(1).SignInAsync(controller.HttpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Is<ClaimsPrincipal>(p => p.FindFirst(UserSession.MethodClaim)!.Value == "oidc"
                && p.FindFirst(UserSession.OidcStampClaim)!.Value == oidc.Stamp),
            Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task RemovingThePasswordNeedsARecentSignInAndAnotherWayToSignIn()
    {
        var oidc = Oidc();
        var (account, user) = Stored();
        var controller = Controller(account, oidc);
        SignIn(controller, user);

        Assert.Equal("LastSignInMethod",
            (await Assert.ThrowsAsync<AccountException>(() => controller.RemovePassword(CancellationToken.None))).Code);

        user = account.Link(new UserReference(user.Id, "alice"), user.Id,
            new ExternalIdentity(oidc.Issuer, "sub-1", "alice"));
        SignIn(controller, user);
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal("RecentSignInRequired",
            (await Assert.ThrowsAsync<AccountException>(() => controller.RemovePassword(CancellationToken.None))).Code);

        Assert.IsType<NoContentResult>(await controller.Reauthenticate(new ConfirmRequest("password"), CancellationToken.None));
        SignIn(controller, account.Find(user.Id)!);
        Assert.IsType<NoContentResult>(await controller.RemovePassword(CancellationToken.None));
        Assert.False(account.Find(user.Id)!.HasPassword);
    }

    [Fact]
    public async Task LinkingSingleSignOnNeedsConfirmationAndIssuesATicketForThisUserVersion()
    {
        var oidc = Oidc();
        var (account, user) = Stored();
        var controller = Controller(account, oidc);
        SignIn(controller, user);
        time.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal("RecentSignInRequired", (await Assert.ThrowsAsync<AccountException>(() =>
            controller.LinkSingleSignOn(new ConfirmRequest("wrong"), CancellationToken.None))).Code);
        var link = Assert.IsType<OkObjectResult>(
            await controller.LinkSingleSignOn(new ConfirmRequest("password"), CancellationToken.None));
        var url = (string)link.Value!.GetType().GetProperty("url")!.GetValue(link.Value)!;
        var ticket = Uri.UnescapeDataString(url["/api/auth/oidc/link?ticket=".Length..]);

        Assert.IsType<EmptyResult>(await controller.LinkSingleSignOnChallenge(ticket, CancellationToken.None));
        await authentication.Received(1).ChallengeAsync(controller.HttpContext, OidcLogin.Scheme,
            Arg.Is<AuthenticationProperties>(p => p.RedirectUri == "/account"
                && p.Items[OidcLogin.LinkUserItem] == user.Id.ToString()
                && p.Items[OidcLogin.LinkStampItem] == user.Stamp));

        // Once the user changes, the ticket no longer works.
        account.SetPassword(new UserReference(user.Id, "alice"), user.Id, "password", "another long password", false);
        SignIn(controller, account.Find(user.Id)!);
        var expired = Assert.IsType<RedirectResult>(
            await controller.LinkSingleSignOnChallenge(ticket, CancellationToken.None));
        Assert.Equal("/account?linkError=expired", expired.Url);
    }

    [Fact]
    public async Task UnlinkingKeepsTheSessionButNeverTheLastSignInMethod()
    {
        var oidc = Oidc();
        var account = Configured();
        var (user, loginId, _) = account.SignInExternal(new ExternalIdentity(oidc.Issuer, "sub-1", "alice"), true);
        var controller = Controller(account, oidc);
        SignIn(controller, user!, UserSession.SingleSignOnMethod, oidc.Stamp);

        Assert.Equal("LastSignInMethod",
            (await Assert.ThrowsAsync<AccountException>(() => controller.Unlink(loginId!.Value, CancellationToken.None))).Code);

        account.SetPassword(new UserReference(user!.Id, "alice"), user.Id, null, "a brand new password", true);
        SignIn(controller, account.Find(user.Id)!, UserSession.SingleSignOnMethod, oidc.Stamp);
        Assert.IsType<NoContentResult>(await controller.Unlink(loginId!.Value, CancellationToken.None));
        Assert.Empty(account.Find(user.Id)!.Logins);
    }

    private (UserAccounts Account, UserRecord User) Stored()
    {
        var account = Unconfigured();
        account.CompleteSetup(account.SetupToken!, "admin", "a long enough password");
        var user = account.Create(new UserReference(1, "admin"), "alice", UserRole.User, "a placeholder password",
            null);
        // Store the short legacy password directly: existing passwords keep working until they are changed.
        store.Tamper(user.Id, u => u with { PasswordHash = Hash });
        account = Unconfigured();
        return (account, account.Find(user.Id)!);
    }

    private UserAccounts Configured() => new(new AdministratorConfiguration(Options.Create(new NamerreOptions
    {
        Authentication = new AuthenticationOptions { Username = "admin", PasswordHash = Hash }
    })), store, Hasher, NullLogger<UserAccounts>.Instance, time);

    private UserAccounts Unconfigured() => new(new AdministratorConfiguration(Options.Create(new NamerreOptions())),
        store, Hasher, NullLogger<UserAccounts>.Instance, time);

    private void SignIn(AuthController controller, UserRecord user, string method = UserSession.PasswordMethod,
        string? oidcStamp = null)
        => controller.ControllerContext.HttpContext.User = UserSession.Create(user, method, oidcStamp, time.GetUtcNow());

    [Fact]
    public async Task SessionAdvertisesConfiguredSingleSignOn()
    {
        antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet("t", "c", "form", "X-XSRF-TOKEN"));
        var controller = Controller(Configured(), Oidc());

        var result = Assert.IsType<OkObjectResult>(await controller.Session(CancellationToken.None));

        Assert.Equivalent(new { oidc = new { displayName = "Keycloak" } }, result.Value);
    }

    [Fact]
    public async Task SingleSignOnIsNotFoundWhenNotConfigured()
        => Assert.IsType<NotFoundResult>(await Controller(Configured()).OidcLogin(CancellationToken.None));

    [Fact]
    public async Task SingleSignOnChallengesTheIdentityProviderWithAFixedReturnAddress()
    {
        var controller = Controller(Configured(), Oidc());

        Assert.IsType<EmptyResult>(await controller.OidcLogin(CancellationToken.None));

        await authentication.Received(1).ChallengeAsync(controller.HttpContext, OidcLogin.Scheme,
            Arg.Is<AuthenticationProperties>(p => p.RedirectUri == "/"));
    }

    [Fact]
    public async Task AFailingChallengeReturnsToTheSignInPage()
    {
        var controller = Controller(Configured(), Oidc());
        authentication.ChallengeAsync(controller.HttpContext, OidcLogin.Scheme, Arg.Any<AuthenticationProperties>())
            .ThrowsAsync(new HttpRequestException("pushed authorization failed"));

        var result = Assert.IsType<RedirectResult>(await controller.OidcLogin(CancellationToken.None));

        Assert.Equal("/?signInError=unavailable", result.Url);
    }

    [Fact]
    public async Task AnUnavailableIdentityProviderReturnsToTheSignInPage()
    {
        providerMetadata.GetConfigurationAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("IDX20803"));

        var result = Assert.IsType<RedirectResult>(
            await Controller(Configured(), Oidc()).OidcLogin(CancellationToken.None));

        Assert.Equal("/?signInError=unavailable", result.Url);
    }

    private static OidcLogin Oidc() => new(Options.Create(new NamerreOptions
    {
        Authentication = new AuthenticationOptions
        {
            Oidc = new OidcOptions
            {
                Authority = "https://keycloak.example/realms/media",
                ClientId = "namerre",
                RequiredRole = "namerre-admin",
                DisplayName = "Keycloak"
            }
        }
    }));

    /// <summary>The controller over the real account requests, sent through the mediator.</summary>
    private AuthController Controller(UserAccounts account, OidcLogin? oidc = null)
    {
        var services = new ServiceCollection()
            .AddApplication()
            .AddSingleton(account)
            .AddSingleton(Substitute.For<IUserHistory>())
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(authentication)
            .BuildServiceProvider();
        var oidcOptions = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        oidcOptions.Get(OidcLogin.Scheme).Returns(new OpenIdConnectOptions { ConfigurationManager = providerMetadata });
        return new AuthController(services.GetRequiredService<ISender>(), oidc ?? new OidcLogin(Options.Create(new NamerreOptions())), oidcOptions,
            antiforgery, NullLogger<AuthController>.Instance, new EphemeralDataProtectionProvider(), time: time)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
    }
}
