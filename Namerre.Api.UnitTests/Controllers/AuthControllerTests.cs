using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Namerre.Application.Authentication;
using Namerre.Application.Renaming;
using Namerre.Authentication;
using Namerre.Controllers;
using Namerre.Infrastructure.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using AuthenticationOptions = Namerre.Infrastructure.Configuration.AuthenticationOptions;

namespace Namerre.Api.UnitTests.Controllers;

public sealed class AuthControllerTests
{
    private static readonly string Hash = AdminAccount.HashPassword("password");
    private readonly IAdministratorStore store = Substitute.For<IAdministratorStore>();
    private readonly IAntiforgery antiforgery = Substitute.For<IAntiforgery>();
    private readonly IAuthenticationService authentication = Substitute.For<IAuthenticationService>();
    private readonly IConfigurationManager<OpenIdConnectConfiguration> providerMetadata =
        Substitute.For<IConfigurationManager<OpenIdConnectConfiguration>>();

    [Fact]
    public void SessionIssuesXsrfCookieAndReportsSetupState()
    {
        antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet("request-token", "cookie-token", "form", "X-XSRF-TOKEN"));
        var controller = Controller(Unconfigured());

        var result = Assert.IsType<OkObjectResult>(controller.Session());

        var cookie = controller.Response.Headers.SetCookie.ToString();
        Assert.Contains("XSRF-TOKEN=request-token", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.DoesNotContain("httponly", cookie);
        Assert.Equivalent(new { username = (string?)null, requiresSetup = true, canChangePassword = false },
            result.Value);
    }

    [Fact]
    public void SessionReportsTheSignedInAdministrator()
    {
        antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet("t", "c", "form", "X-XSRF-TOKEN"));
        var controller = Controller(Configured());
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme));

        var result = Assert.IsType<OkObjectResult>(controller.Session());

        Assert.Equivalent(new { username = "admin", requiresSetup = false, canChangePassword = false },
            result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetupRequiresAUsername(string username)
    {
        var account = Unconfigured();

        Assert.IsType<BadRequestObjectResult>(Controller(account).Setup(
            new SetupRequest(account.SetupToken!, username, "password")));
        Assert.True(account.RequiresSetup);
    }

    [Fact]
    public void SetupWithInvalidTokenIsForbidden()
    {
        var result = Controller(Unconfigured()).Setup(new SetupRequest(new string('0', 64), "admin", "password"));

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void SetupAfterConfigurationConflicts()
        => Assert.IsType<ConflictObjectResult>(Controller(Configured())
            .Setup(new SetupRequest(new string('0', 64), "admin", "password")));

    [Fact]
    public void SuccessfulSetupReturnsNoContent()
    {
        store.TryCreate(Arg.Any<AdminCredentials>()).Returns(true);
        var account = Unconfigured();

        Assert.IsType<NoContentResult>(Controller(account).Setup(
            new SetupRequest(account.SetupToken!, "admin", "password")));
        Assert.False(account.RequiresSetup);
    }

    public static TheoryData<Exception> StorageFailures => new()
    {
        new IOException("disk"),
        new UnauthorizedAccessException("denied"),
        new RenameException("DataUnavailable", "db"),
        new AdministratorStorageException("db", new IOException())
    };

    [Theory]
    [MemberData(nameof(StorageFailures))]
    public void StorageFailuresDuringSetupAreServiceUnavailable(Exception failure)
    {
        store.TryCreate(Arg.Any<AdminCredentials>()).Throws(failure);
        var account = Unconfigured();

        var result = Controller(account).Setup(new SetupRequest(account.SetupToken!, "admin", "password"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.True(account.RequiresSetup);
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("other", "password")]
    public async Task LoginWithInvalidCredentialsIsUnauthorized(string username, string password)
    {
        var result = await Controller(Configured()).Login(new LoginRequest(username, password));

        Assert.IsType<UnauthorizedObjectResult>(result);
        await authentication.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task LoginSignsInWithACredentialStamp()
    {
        var account = Configured();
        var controller = Controller(account);

        Assert.IsType<NoContentResult>(await controller.Login(new LoginRequest("admin", "password")));

        await authentication.Received(1).SignInAsync(controller.HttpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Is<ClaimsPrincipal>(p => p.Identity!.Name == "admin"
                && p.FindFirst("credential-stamp")!.Value == account.Stamp),
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
        var account = Stored();
        var stamp = account.Stamp;
        var controller = Controller(account);

        Assert.IsType<NoContentResult>(await controller.ChangePassword(
            new ChangePasswordRequest("password", "new password")));

        Assert.True(account.Verify("admin", "new password"));
        Assert.NotEqual(stamp, account.Stamp);
        await authentication.Received(1).SignInAsync(controller.HttpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Is<ClaimsPrincipal>(p => p.Identity!.Name == "admin"
                && p.FindFirst("credential-stamp")!.Value == account.Stamp),
            Arg.Is<AuthenticationProperties>(p => !p.IsPersistent));
    }

    [Fact]
    public async Task ChangingThePasswordWithAWrongCurrentPasswordIsForbidden()
    {
        var account = Stored();

        var result = await Controller(account).ChangePassword(new ChangePasswordRequest("wrong", "new password"));

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.True(account.Verify("admin", "password"));
        await authentication.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task ConfiguredPasswordsCannotBeChangedThroughTheApi()
        => Assert.IsType<ConflictObjectResult>(await Controller(Configured())
            .ChangePassword(new ChangePasswordRequest("password", "new password")));

    [Theory]
    [MemberData(nameof(StorageFailures))]
    public async Task StorageFailuresDuringPasswordChangeAreServiceUnavailable(Exception failure)
    {
        var account = Stored();
        store.TryUpdate(Arg.Any<AdminCredentials>(), Arg.Any<string>()).Throws(failure);

        var result = await Controller(account).ChangePassword(new ChangePasswordRequest("password", "new password"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.True(account.Verify("admin", "password"));
    }

    private AdminAccount Stored()
    {
        var credentials = new AdminCredentials("admin", Hash);
        store.Load().Returns(credentials);
        store.TryUpdate(credentials, Arg.Any<string>()).Returns(true);
        return Unconfigured();
    }

    private AdminAccount Configured() => new(Options.Create(new NamerreOptions
    {
        Authentication = new AuthenticationOptions { Username = "admin", PasswordHash = Hash }
    }), store);

    private AdminAccount Unconfigured() => new(Options.Create(new NamerreOptions()), store);

    [Fact]
    public void SessionAdvertisesConfiguredSingleSignOn()
    {
        antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet("t", "c", "form", "X-XSRF-TOKEN"));
        var controller = Controller(Configured(), Oidc());

        var result = Assert.IsType<OkObjectResult>(controller.Session());

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

    [Fact]
    public async Task SingleSignOnSessionsCannotChangeTheLocalPassword()
    {
        antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet("t", "c", "form", "X-XSRF-TOKEN"));
        var account = Stored();
        var oidc = Oidc();
        var controller = Controller(account, oidc);
        controller.ControllerContext.HttpContext.User = oidc.CreateSessionPrincipal(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "subject-1"),
                new Claim("realm_access", """{"roles":["namerre-admin"]}""")], "oidc")))!;

        var session = Assert.IsType<OkObjectResult>(controller.Session());
        var result = await controller.ChangePassword(new ChangePasswordRequest("password", "new password"));

        Assert.Equivalent(new { canChangePassword = false }, session.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.True(account.Verify("admin", "password"));
        await authentication.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
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

    private AuthController Controller(AdminAccount account, OidcLogin? oidc = null)
    {
        var services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        var oidcOptions = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        oidcOptions.Get(OidcLogin.Scheme).Returns(new OpenIdConnectOptions { ConfigurationManager = providerMetadata });
        return new AuthController(account, oidc ?? new OidcLogin(Options.Create(new NamerreOptions())), oidcOptions,
            antiforgery, NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
    }
}
