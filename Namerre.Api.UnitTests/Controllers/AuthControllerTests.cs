using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        Assert.Equivalent(new { username = (string?)null, requiresSetup = true }, result.Value);
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

        Assert.Equivalent(new { username = "admin", requiresSetup = false }, result.Value);
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

        Assert.IsType<NoContentResult>(await controller.Logout());

        await authentication.Received(1).SignOutAsync(controller.HttpContext,
            CookieAuthenticationDefaults.AuthenticationScheme, Arg.Any<AuthenticationProperties?>());
    }

    private AdminAccount Configured() => new(Options.Create(new NamerreOptions
    {
        Authentication = new AuthenticationOptions { Username = "admin", PasswordHash = Hash }
    }), store);

    private AdminAccount Unconfigured() => new(Options.Create(new NamerreOptions()), store);

    private AuthController Controller(AdminAccount account)
    {
        var services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        return new AuthController(account, antiforgery, NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
    }
}
