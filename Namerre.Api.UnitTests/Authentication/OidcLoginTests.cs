using System.Security.Claims;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Authentication;
using Namerre.Infrastructure.Configuration;
using NSubstitute;
using Xunit;
using AuthenticationOptions = Namerre.Infrastructure.Configuration.AuthenticationOptions;

namespace Namerre.Api.UnitTests.Authentication;

public sealed class OidcLoginTests
{
    [Fact]
    public void IsDisabledWithoutAnAuthority()
    {
        var oidc = Create(new OidcOptions());

        Assert.False(oidc.Enabled);
        Assert.Null(oidc.CreateSessionPrincipal(External(("sub", "1"))));
        Assert.False(oidc.IsValidSession(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(OidcLogin.CredentialStampClaim, "")]))));
    }

    public static TheoryData<OidcOptions, string> InvalidSettings => new()
    {
        { new OidcOptions { ClientId = "namerre" }, "Authority" },
        { Valid(options => options.Authority = "keycloak/realms/media"), "Authority" },
        { Valid(options => options.Authority = "http://keycloak/realms/media"), "Authority" },
        { Valid(options => options.ClientId = " "), "ClientId" },
        { Valid(options => options.RequiredRole = ""), "RequiredRole" },
        { Valid(options => options.RoleClaim = "realm_access..roles"), "RoleClaim" },
        { Valid(options => options.UsernameClaim = ""), "UsernameClaim" },
        { Valid(options => options.DisplayName = new string('x', 65)), "DisplayName" }
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void InvalidSettingsAreRejected(OidcOptions options, string setting)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Create(options));
        Assert.Contains("Namerre:Authentication:Oidc:" + setting, error.Message);
    }

    [Fact]
    public void PlainHttpAuthorityIsAllowedOnlyWhenHttpsMetadataIsNotRequired()
        => Assert.True(Create(Valid(options =>
        {
            options.Authority = "http://keycloak:8080/realms/media";
            options.RequireHttpsMetadata = false;
        })).Enabled);

    [Theory]
    [InlineData("realm_access.roles", "realm_access", """{"roles":["user","namerre-admin"]}""")]
    [InlineData("resource_access.namerre.roles", "resource_access", """{"namerre":{"roles":["namerre-admin"]}}""")]
    [InlineData("roles", "roles", "namerre-admin")]
    [InlineData("roles", "roles", """["namerre-admin"]""")]
    [InlineData("groups", "groups", """{"x":1}""")]
    public void FindsRolesInFlatOrNestedClaims(string path, string type, string value)
    {
        var expected = type != "groups";
        Assert.Equal(expected, OidcLogin.HasRole(External((type, value)), path, "namerre-admin"));
    }

    [Theory]
    [InlineData("""{"roles":["namerre-admin-2"]}""")]
    [InlineData("""{"roles":"NAMERRE-ADMIN"}""")]
    [InlineData("""{"other":["namerre-admin"]}""")]
    [InlineData("{not json")]
    public void MissingOrDifferentRolesAreNotAccepted(string value)
        => Assert.False(OidcLogin.HasRole(External(("realm_access", value)), "realm_access.roles", "namerre-admin"));

    [Fact]
    public void CreatesAMinimalStampedSessionForUsersWithTheRole()
    {
        var oidc = Create(Valid());

        var principal = oidc.CreateSessionPrincipal(External(("sub", "subject-1"),
            ("preferred_username", "alice"), ("email", "alice@example.com"),
            ("realm_access", """{"roles":["namerre-admin"]}""")));

        Assert.NotNull(principal);
        Assert.Equal("alice", principal.Identity!.Name);
        Assert.Equal("subject-1", principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        Assert.Null(principal.FindFirst("email"));
        Assert.True(oidc.IsValidSession(principal));
        Assert.True(AuthenticationSetup.IsValidSession(principal, UnconfiguredAccount(), oidc));
    }

    [Fact]
    public void FallsBackToTheSubjectWhenTheUsernameClaimIsMissing()
        => Assert.Equal("subject-1", Create(Valid()).CreateSessionPrincipal(External(("sub", "subject-1"),
            ("realm_access", """{"roles":["namerre-admin"]}""")))!.Identity!.Name);

    [Fact]
    public void RejectsUsersWithoutTheRole()
        => Assert.Null(Create(Valid()).CreateSessionPrincipal(External(("sub", "subject-1"),
            ("preferred_username", "mallory"), ("realm_access", """{"roles":["user"]}"""))));

    [Fact]
    public void ChangingAccessSettingsInvalidatesSessions()
    {
        var principal = Create(Valid()).CreateSessionPrincipal(External(("sub", "1"),
            ("realm_access", """{"roles":["namerre-admin","other"]}""")))!;

        Assert.False(Create(Valid(options => options.RequiredRole = "other")).IsValidSession(principal));
        Assert.False(AuthenticationSetup.IsValidSession(principal, UnconfiguredAccount(), Create(new OidcOptions())));
    }

    [Fact]
    public void LocalStampsDoNotValidateSingleSignOnSessionsOrViceVersa()
    {
        var oidc = Create(Valid());
        var local = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(OidcLogin.CredentialStampClaim, oidc.Stamp)], "Cookies"));

        Assert.False(AuthenticationSetup.IsValidSession(local, UnconfiguredAccount(), oidc));
    }

    private static OidcLogin Create(OidcOptions options) => new(Options.Create(new NamerreOptions
    {
        Authentication = new AuthenticationOptions { Oidc = options }
    }));

    private static OidcOptions Valid(Action<OidcOptions>? configure = null)
    {
        var options = new OidcOptions
        {
            Authority = "https://keycloak.example/realms/media",
            ClientId = "namerre",
            RequiredRole = "namerre-admin"
        };
        configure?.Invoke(options);
        return options;
    }

    private static ClaimsPrincipal External(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "oidc"));

    private static AdminAccount UnconfiguredAccount()
        => new(Options.Create(new NamerreOptions()), Substitute.For<IAdministratorStore>());
}
