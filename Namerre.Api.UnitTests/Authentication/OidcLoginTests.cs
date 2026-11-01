using System.Security.Claims;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Authentication;
using Namerre.Infrastructure.Authentication;
using Namerre.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Null(oidc.Read(External(("sub", "1"))));
        Assert.False(oidc.IsValidSession(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(UserSession.OidcStampClaim, "")]))));
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
    public void ReadsTheStableSubjectOfIdentitiesWithTheRole()
    {
        var identity = Create(Valid(options => options.Authority += "/")).Read(External(("sub", "subject-1"),
            ("preferred_username", "alice"), ("email", "alice@example.com"),
            ("realm_access", """{"roles":["namerre-admin"]}""")));

        Assert.Equal(new ExternalIdentity("https://keycloak.example/realms/media", "subject-1", "alice"), identity);
    }

    [Fact]
    public void FallsBackToTheSubjectWhenTheUsernameClaimIsMissing()
        => Assert.Equal("subject-1", Create(Valid()).Read(External(("sub", "subject-1"),
            ("realm_access", """{"roles":["namerre-admin"]}""")))!.Username);

    [Fact]
    public void RejectsUsersWithoutTheRoleOrASubject()
    {
        Assert.Null(Create(Valid()).Read(External(("sub", "subject-1"),
            ("preferred_username", "mallory"), ("realm_access", """{"roles":["user"]}"""))));
        Assert.Null(Create(Valid()).Read(External(("preferred_username", "mallory"),
            ("realm_access", """{"roles":["namerre-admin"]}"""))));
    }

    [Fact]
    public void SessionsOfLocalUsersAreValidUntilTheirStampOrSingleSignOnSettingsChange()
    {
        var oidc = Create(Valid());
        var store = new InMemoryUserStore();
        var hasher = new PasswordHasher();
        var accounts = new UserAccounts(new AdministratorConfiguration(Options.Create(new NamerreOptions
        {
            Authentication = new AuthenticationOptions { Username = "root", PasswordHash = hasher.Hash("a long password") }
        })), store, hasher, NullLogger<UserAccounts>.Instance);
        var (user, _, _) = accounts.SignInExternal(new ExternalIdentity(oidc.Issuer, "1", "alice"), true);
        var principal = UserSession.Create(user!, UserSession.SingleSignOnMethod, oidc.Stamp, DateTimeOffset.UtcNow);
        var local = UserSession.Create(user!, UserSession.PasswordMethod, null, DateTimeOffset.UtcNow);

        Assert.True(AuthenticationSetup.IsValidSession(principal, accounts, oidc));
        Assert.True(AuthenticationSetup.IsValidSession(local, accounts, oidc));
        Assert.False(AuthenticationSetup.IsValidSession(principal, accounts,
            Create(Valid(options => options.RequiredRole = "other"))));
        Assert.False(AuthenticationSetup.IsValidSession(principal, accounts, Create(new OidcOptions())));

        accounts.Update(new UserReference(99, "admin"), user!.Id, UserRole.Admin, null);

        Assert.False(AuthenticationSetup.IsValidSession(principal, accounts, oidc));
        Assert.False(AuthenticationSetup.IsValidSession(local, accounts, oidc));
    }

    [Fact]
    public void ARecentSignInIsTheProvidersAuthenticationTime()
    {
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var earlier = now.AddHours(-3);

        Assert.Equal(earlier, AuthenticationSetup.AuthenticatedAt(
            External(("auth_time", earlier.ToUnixTimeSeconds().ToString())), now));
        // Missing, malformed or future times count as now.
        Assert.Equal(now, AuthenticationSetup.AuthenticatedAt(External(("sub", "1")), now));
        Assert.Equal(now, AuthenticationSetup.AuthenticatedAt(External(("auth_time", "soon")), now));
        Assert.Equal(now, AuthenticationSetup.AuthenticatedAt(
            External(("auth_time", now.AddHours(1).ToUnixTimeSeconds().ToString())), now));
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
}
