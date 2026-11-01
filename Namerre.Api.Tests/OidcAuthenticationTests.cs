using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Namerre.Authentication;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class OidcAuthenticationTests
{
    // Each provider gets its own data directory, since single sign-on users are stored.
    private const string Issuer = "https://keycloak.test/realms/media";
    private const string ClientId = "namerre";
    private const string Role = "namerre-admin";

    [Fact]
    public async Task SingleSignOnIsUnavailableUnlessConfigured()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);

        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        using var login = await client.GetAsync("/api/auth/oidc/login");
        using var callback = await client.GetAsync(OidcLogin.CallbackPath);

        Assert.Equal(JsonValueKind.Null, session.GetProperty("oidc").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, callback.StatusCode);
    }

    [Fact]
    public void IncompleteConfigurationPreventsStartup()
    {
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Namerre:Authentication:Oidc:Authority"] = Issuer })));
        var error = Assert.Throws<InvalidOperationException>(() => { _ = factory.Server; });
        Assert.Contains("Namerre:Authentication:Oidc:ClientId", error.Message);
    }

    [Fact]
    public async Task UsersWithTheRequiredRoleSignInWithCodeFlowAndPkce()
    {
        using var provider = new FakeProvider([Role, "offline_access"]);
        using var factory = provider.Factory();
        using var client = Client(factory);

        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("Keycloak", session.GetProperty("oidc").GetProperty("displayName").GetString());

        using var callback = await provider.SignIn(client);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        var cookie = Assert.Single(callback.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-namerre-session=", StringComparison.Ordinal));
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Equal("alice", (await client.GetFromJsonAsync<JsonElement>("/api/auth/session"))
            .GetProperty("username").GetString());
        using var catalog = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Contains("code_verifier=", provider.TokenRequest);
        Assert.Contains("client_secret=", provider.TokenRequest);
    }

    [Fact]
    public async Task UsersWithoutTheRequiredRoleAreRejected()
    {
        using var provider = new FakeProvider(["user"]);
        using var factory = provider.Factory();
        using var client = Client(factory);

        using var callback = await provider.SignIn(client);

        Assert.Equal("/?signInError=access-denied", callback.Headers.Location!.OriginalString);
        Assert.False(callback.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(value => value.StartsWith("__Host-namerre-session=", StringComparison.Ordinal) &&
                !value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)));
        using var denied = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task ForgedOrFailedCallbacksAreRejected()
    {
        using var provider = new FakeProvider([Role]);
        using var factory = provider.Factory();
        using var client = Client(factory);

        using var forged = await client.PostAsync(OidcLogin.CallbackPath, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["code"] = "code", ["state"] = "forged" }));
        using var error = await client.GetAsync(OidcLogin.CallbackPath + "?error=access_denied");

        Assert.Equal("/?signInError=failed", forged.Headers.Location!.OriginalString);
        Assert.Equal("/?signInError=failed", error.Headers.Location!.OriginalString);
        Assert.Null(provider.TokenRequest);
    }

    [Fact]
    public async Task AFailingPushedAuthorizationRequestReturnsToTheSignInPage()
    {
        using var provider = new FakeProvider([Role]) { PushedAuthorizationFails = true };
        using var factory = provider.Factory();
        using var client = Client(factory);

        using var login = await client.GetAsync("/api/auth/oidc/login");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/?signInError=unavailable", login.Headers.Location!.OriginalString);
        Assert.StartsWith("client_id=", provider.TokenRequest);
    }

    [Fact]
    public async Task SingleSignOnSessionsEndWhenAccessSettingsChange()
    {
        using var provider = new FakeProvider([Role]);
        string cookie;
        using (var factory = provider.Factory())
        using (var client = Client(factory))
        {
            using var callback = await provider.SignIn(client);
            cookie = callback.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("__Host-namerre-session=", StringComparison.Ordinal)).Split(';')[0];
        }

        // The data directory belongs to one host at a time, so the hosts run one after another.
        HttpStatusCode accepted, rejected;
        using (var unchanged = provider.Factory())
        using (var same = AuthWebApplicationFactory.AnonymousClient(unchanged))
        {
            same.DefaultRequestHeaders.Add("Cookie", cookie);
            using var response = await same.GetAsync("/api/rename-plugins");
            accepted = response.StatusCode;
        }
        provider.Settings["Namerre:Authentication:Oidc:RequiredRole"] = "other";
        using (var changed = provider.Factory())
        using (var other = AuthWebApplicationFactory.AnonymousClient(changed))
        {
            other.DefaultRequestHeaders.Add("Cookie", cookie);
            using var response = await other.GetAsync("/api/rename-plugins");
            rejected = response.StatusCode;
        }

        // A separate host with unchanged settings accepts the cookie, so only the access stamp rejects it.
        Assert.Equal(HttpStatusCode.OK, accepted);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected);
    }

    [Fact]
    public async Task FirstSignInCreatesALocalUserWhoseRoleIsManagedInNamerre()
    {
        using var provider = new FakeProvider([Role]);
        using var factory = provider.Factory();
        using var admin = await AuthWebApplicationFactory.AuthenticatedClient(factory);

        using (var client = Client(factory))
        {
            using var callback = await provider.SignIn(client);
            var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
            Assert.Equal(("alice", "User", false), (session.GetProperty("username").GetString(),
                session.GetProperty("role").GetString(), session.GetProperty("hasPassword").GetBoolean()));
            Assert.Equal(1, session.GetProperty("logins").GetArrayLength());
            // Users have no administrator rights.
            using var users = await client.GetAsync("/api/users");
            using var watchFolders = await client.GetAsync("/api/watch-folders");
            using var deletions = await client.GetAsync("/api/rename-run-deletions/candidates");
            Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, watchFolders.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deletions.StatusCode);
        }

        var alice = await FindUser(admin, "alice");
        Assert.Equal(Issuer, alice.GetProperty("logins")[0].GetProperty("issuer").GetString());
        using var promote = await admin.PatchAsJsonAsync($"/api/users/{alice.GetProperty("id").GetInt64()}",
            new { role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        // The identity provider does not hold the Namerre role, yet the promotion survives the next sign-in.
        using var again = Client(factory);
        using var secondCallback = await provider.SignIn(again);
        var promoted = await again.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("Admin", promoted.GetProperty("role").GetString());
        using var allowed = await again.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(2, (await allowed.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Fact]
    public async Task IdentitiesAreNeverLinkedToExistingUsersByUsername()
    {
        using var provider = new FakeProvider([Role]);
        using var factory = provider.Factory();
        using var admin = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        using var create = await admin.PostAsJsonAsync("/api/users",
            new { username = "alice", role = "Admin", password = "alice-local-password" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var client = Client(factory);

        using var callback = await provider.SignIn(client);

        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(("alice-2", "User"), (session.GetProperty("username").GetString(),
            session.GetProperty("role").GetString()));
        Assert.Equal(0, (await FindUser(admin, "alice")).GetProperty("logins").GetArrayLength());
    }

    [Fact]
    public async Task WithoutProvisioningUnlinkedIdentitiesWaitForAnAdministratorToLinkThem()
    {
        using var provider = new FakeProvider([Role]);
        provider.Settings["Namerre:Authentication:Oidc:AutoProvision"] = "false";
        using var factory = provider.Factory();
        using var admin = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        using var client = Client(factory);

        using var refused = await provider.SignIn(client);
        Assert.Equal("/?signInError=not-linked", refused.Headers.Location!.OriginalString);
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>("/api/users")).GetArrayLength());

        using var create = await admin.PostAsJsonAsync("/api/users",
            new { username = "bob", password = "bob-local-password" });
        var bob = await create.Content.ReadFromJsonAsync<JsonElement>();
        using var link = await admin.PostAsJsonAsync($"/api/users/{bob.GetProperty("id").GetInt64()}/logins",
            new { subject = "subject-1", displayName = "alice" });
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);

        using var accepted = await provider.SignIn(client);
        Assert.Equal("/", accepted.Headers.Location!.OriginalString);
        Assert.Equal("bob", (await client.GetFromJsonAsync<JsonElement>("/api/auth/session"))
            .GetProperty("username").GetString());
    }

    [Fact]
    public async Task DisabledUsersCannotSignInWithSingleSignOn()
    {
        using var provider = new FakeProvider([Role]);
        using var factory = provider.Factory();
        using var admin = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        using var client = Client(factory);
        using (await provider.SignIn(client)) { }
        var alice = await FindUser(admin, "alice");

        using var disable = await admin.PatchAsJsonAsync($"/api/users/{alice.GetProperty("id").GetInt64()}",
            new { disabled = true });
        using var ended = await client.GetAsync("/api/rename-plugins");
        using var refused = await provider.SignIn(client);

        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ended.StatusCode);
        Assert.Equal("/?signInError=access-denied", refused.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task SignedInUsersLinkSingleSignOnButNotAnIdentityLinkedElsewhere()
    {
        using var provider = new FakeProvider([Role]);
        using var factory = provider.Factory();
        using var admin = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        using (var alice = Client(factory))
        using (await provider.SignIn(alice)) { }
        using var create = await admin.PostAsJsonAsync("/api/users",
            new { username = "bob", password = "bob-local-password" });
        using var bob = Client(factory);
        await AuthWebApplicationFactory.RefreshCsrf(bob);
        using (var login = await bob.PostAsJsonAsync("/api/auth/login",
                   new { username = "bob", password = "bob-local-password" }))
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await AuthWebApplicationFactory.RefreshCsrf(bob);

        // Alice's identity is taken.
        using var taken = await provider.SignIn(bob, await LinkUrl(bob));
        Assert.Equal("/account?linkError=taken", taken.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/rename-plugins")).StatusCode);

        provider.Subject = "subject-2";
        provider.Username = "bob-at-keycloak";
        using var linked = await provider.SignIn(bob, await LinkUrl(bob));
        Assert.Equal("/account?linked=1", linked.Headers.Location!.OriginalString);
        var session = await bob.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("bob", session.GetProperty("username").GetString());
        Assert.Equal("bob-at-keycloak", session.GetProperty("logins")[0].GetProperty("displayName").GetString());

        using var viaSso = Client(factory);
        using (await provider.SignIn(viaSso)) { }
        Assert.Equal("bob", (await viaSso.GetFromJsonAsync<JsonElement>("/api/auth/session"))
            .GetProperty("username").GetString());
    }

    [Fact]
    public async Task SingleSignOnUsersCanAddAPasswordButNotRemoveTheirLastSignInMethod()
    {
        using var provider = new FakeProvider([Role]);
        using var factory = provider.Factory();
        using var client = Client(factory);
        using (await provider.SignIn(client)) { }
        await AuthWebApplicationFactory.RefreshCsrf(client);
        var loginId = (await client.GetFromJsonAsync<JsonElement>("/api/auth/session"))
            .GetProperty("logins")[0].GetProperty("id").GetInt64();

        using var unlink = await client.DeleteAsync($"/api/auth/logins/{loginId}");
        Assert.Equal(HttpStatusCode.Conflict, unlink.StatusCode);
        Assert.Equal("LastSignInMethod", (await unlink.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());

        using var setPassword = await client.PostAsJsonAsync("/api/auth/password",
            new { newPassword = "alice-new-password" });
        Assert.Equal(HttpStatusCode.NoContent, setPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/rename-plugins")).StatusCode);

        using var local = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(local);
        using var login = await local.PostAsJsonAsync("/api/auth/login",
            new { username = "alice", password = "alice-new-password" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var unlinked = await client.DeleteAsync($"/api/auth/logins/{loginId}");
        Assert.Equal(HttpStatusCode.NoContent, unlinked.StatusCode);
    }

    [Fact]
    public async Task ConfirmingWithSingleSignOnNeedsTheProviderToAuthenticateAgain()
    {
        using var provider = new FakeProvider([Role]) { AuthTime = DateTimeOffset.UtcNow.AddHours(-2) };
        using var factory = provider.Factory();
        using var client = Client(factory);

        // A provider session reused from two hours ago signs in, but is not a recent sign-in.
        using (await provider.SignIn(client)) { }
        await AuthWebApplicationFactory.RefreshCsrf(client);
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/session"))
            .GetProperty("recentSignIn").GetBoolean());
        using var refused = await client.PostAsJsonAsync("/api/auth/password",
            new { newPassword = "alice-new-password" }); // betterleaks:allow
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // Signing in again from the account page asks for a fresh authentication.
        provider.AuthTime = DateTimeOffset.UtcNow;
        using var again = await provider.SignIn(client, "/api/auth/oidc/login?returnTo=account");
        Assert.Equal("/account", again.Headers.Location!.OriginalString);
        Assert.Equal(("login", "0"), (provider.AuthorizationQuery!["prompt"], provider.AuthorizationQuery["max_age"]));
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var accepted = await client.PostAsJsonAsync("/api/auth/password",
            new { newPassword = "alice-new-password" }); // betterleaks:allow
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task SingleSignOnCannotProvisionUsersBeforeInstallation()
    {
        using var provider = new FakeProvider([Role]);
        provider.Settings["Namerre:Authentication:Username"] = "";
        provider.Settings["Namerre:Authentication:PasswordHash"] = "";
        using var factory = provider.Factory();
        using var client = Client(factory);

        using var refused = await provider.SignIn(client);

        Assert.Equal("/?signInError=setup-required", refused.Headers.Location!.OriginalString);
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/auth/session"))
            .GetProperty("requiresSetup").GetBoolean());
    }

    private static async Task<string> LinkUrl(HttpClient client)
    {
        using var start = await client.PostAsJsonAsync("/api/auth/oidc/link", new { });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        return (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
    }

    private static async Task<JsonElement> FindUser(HttpClient admin, string username)
        => (await admin.GetFromJsonAsync<JsonElement>("/api/users"))!.EnumerateArray()
            .Single(user => user.GetProperty("username").GetString() == username);

    [Fact]
    public async Task SigningOutAlsoEndsTheProviderSession()
    {
        using var provider = new FakeProvider([Role]) { HasEndSessionEndpoint = true };
        using var factory = provider.Factory();
        using var client = Client(factory);
        using var callback = await provider.SignIn(client);
        await AuthWebApplicationFactory.RefreshCsrf(client);

        using var logout = await client.PostAsJsonAsync("/api/auth/logout", new { });

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        var endSession = new Uri((await logout.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("endSessionUrl").GetString()!);
        Assert.StartsWith(Issuer + "/protocol/openid-connect/logout?", endSession.AbsoluteUri);
        var query = HttpUtility.ParseQueryString(endSession.Query);
        Assert.Equal(provider.IdToken, query["id_token_hint"]);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal("https://localhost/", query["post_logout_redirect_uri"]);
        using var denied = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task SigningOutEndsOnlyTheNamerreSessionWithoutAProviderLogoutEndpoint()
    {
        using var provider = new FakeProvider([Role]);
        using var factory = provider.Factory();
        using var client = Client(factory);
        using var callback = await provider.SignIn(client);
        await AuthWebApplicationFactory.RefreshCsrf(client);

        using var logout = await client.PostAsJsonAsync("/api/auth/logout", new { });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var denied = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private sealed class FakeProvider(string[] roles) : HttpMessageHandler
    {
        private readonly RsaSecurityKey key = new(RSA.Create(2048)) { KeyId = "test" };
        private readonly string workspace = Path.Combine(Path.GetTempPath(), $"namerre-oidc-{Guid.NewGuid():N}");
        private string? nonce;

        public string Subject { get; set; } = "subject-1";
        public string Username { get; set; } = "alice";
        public Dictionary<string, string?> Settings { get; } = [];

        public string? TokenRequest { get; private set; }
        public string? IdToken { get; private set; }
        public bool PushedAuthorizationFails { get; init; }
        public bool HasEndSessionEndpoint { get; init; }
        /// <summary>The <c>auth_time</c> claim to issue, when set.</summary>
        public DateTimeOffset? AuthTime { get; set; }
        /// <summary>The query of the last authorization request.</summary>
        public System.Collections.Specialized.NameValueCollection? AuthorizationQuery { get; private set; }

        public WebApplicationFactory<Program> Factory() => new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Namerre:Authentication:Oidc:Authority"] = Issuer,
                        ["Namerre:Authentication:Oidc:ClientId"] = ClientId,
                        ["Namerre:Authentication:Oidc:ClientSecret"] = "secret",
                        ["Namerre:Authentication:Oidc:RequiredRole"] = Role,
                        ["Namerre:Authentication:Oidc:DisplayName"] = "Keycloak",
                        ["Namerre:RootPath"] = Directory.CreateDirectory(Path.Combine(workspace, "media")).FullName,
                        ["Namerre:DataPath"] = Path.Combine(workspace, "data")
                    }).AddInMemoryCollection(Settings))
                .ConfigureTestServices(services =>
                    services.PostConfigure<OpenIdConnectOptions>(OidcLogin.Scheme, options =>
                    {
                        var configuration = new OpenIdConnectConfiguration
                        {
                            Issuer = Issuer,
                            AuthorizationEndpoint = Issuer + "/protocol/openid-connect/auth",
                            TokenEndpoint = Issuer + "/protocol/openid-connect/token"
                        };
                        if (PushedAuthorizationFails)
                            configuration.PushedAuthorizationRequestEndpoint = Issuer + "/protocol/openid-connect/ext/par/request";
                        if (HasEndSessionEndpoint)
                            configuration.EndSessionEndpoint = Issuer + "/protocol/openid-connect/logout";
                        configuration.SigningKeys.Add(key);
                        options.Configuration = configuration;
                        options.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                        options.Backchannel = new HttpClient(this);
                    })));

        public async Task<HttpResponseMessage> SignIn(HttpClient client, string start = "/api/auth/oidc/login")
        {
            using var challenge = await client.GetAsync(start);
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            var location = challenge.Headers.Location!;
            Assert.StartsWith(Issuer + "/protocol/openid-connect/auth?", location.AbsoluteUri);
            var query = HttpUtility.ParseQueryString(location.Query);
            Assert.Equal("code", query["response_type"]);
            Assert.Equal("S256", query["code_challenge_method"]);
            Assert.Equal("https://localhost" + OidcLogin.CallbackPath, query["redirect_uri"]);
            nonce = query["nonce"];
            AuthorizationQuery = query;
            return await client.PostAsync(OidcLogin.CallbackPath, new FormUrlEncodedContent(
                new Dictionary<string, string> { ["code"] = "authorization-code", ["state"] = query["state"]! }));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            TokenRequest = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (PushedAuthorizationFails)
                throw new HttpRequestException("Connection refused.");
            IdToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = ClientId,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = Subject,
                    ["preferred_username"] = Username,
                    ["nonce"] = nonce!,
                    ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles }
                }.Concat(AuthTime is { } authTime
                    ? [new KeyValuePair<string, object>("auth_time", authTime.ToUnixTimeSeconds())]
                    : []).ToDictionary(),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    access_token = "access-token",
                    token_type = "Bearer",
                    id_token = IdToken,
                    expires_in = 300
                })
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(workspace))
                Directory.Delete(workspace, recursive: true);
        }
    }
}
