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
    public async Task SingleSignOnSessionsEndWhenAccessSettingsChange()
    {
        using var provider = new FakeProvider([Role]);
        using var factory = provider.Factory();
        using var client = Client(factory);
        using var callback = await provider.SignIn(client);
        var cookie = callback.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-namerre-session=", StringComparison.Ordinal)).Split(';')[0];
        using var changed = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Namerre:Authentication:Oidc:RequiredRole"] = "other" })));
        using var unchanged = factory.WithWebHostBuilder(_ => { });
        using var other = AuthWebApplicationFactory.AnonymousClient(changed);
        using var same = AuthWebApplicationFactory.AnonymousClient(unchanged);
        other.DefaultRequestHeaders.Add("Cookie", cookie);
        same.DefaultRequestHeaders.Add("Cookie", cookie);

        // A separate host with unchanged settings accepts the cookie, so only the access stamp rejects it.
        using var accepted = await same.GetAsync("/api/rename-plugins");
        using var rejected = await other.GetAsync("/api/rename-plugins");

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
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
        private string? nonce;

        public string? TokenRequest { get; private set; }

        public WebApplicationFactory<Program> Factory() => new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Namerre:Authentication:Oidc:Authority"] = Issuer,
                        ["Namerre:Authentication:Oidc:ClientId"] = ClientId,
                        ["Namerre:Authentication:Oidc:ClientSecret"] = "secret",
                        ["Namerre:Authentication:Oidc:RequiredRole"] = Role,
                        ["Namerre:Authentication:Oidc:DisplayName"] = "Keycloak"
                    }))
                .ConfigureTestServices(services =>
                    services.PostConfigure<OpenIdConnectOptions>(OidcLogin.Scheme, options =>
                    {
                        var configuration = new OpenIdConnectConfiguration
                        {
                            Issuer = Issuer,
                            AuthorizationEndpoint = Issuer + "/protocol/openid-connect/auth",
                            TokenEndpoint = Issuer + "/protocol/openid-connect/token"
                        };
                        configuration.SigningKeys.Add(key);
                        options.Configuration = configuration;
                        options.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                        options.Backchannel = new HttpClient(this);
                    })));

        public async Task<HttpResponseMessage> SignIn(HttpClient client)
        {
            using var challenge = await client.GetAsync("/api/auth/oidc/login");
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            var location = challenge.Headers.Location!;
            Assert.StartsWith(Issuer + "/protocol/openid-connect/auth?", location.AbsoluteUri);
            var query = HttpUtility.ParseQueryString(location.Query);
            Assert.Equal("code", query["response_type"]);
            Assert.Equal("S256", query["code_challenge_method"]);
            Assert.Equal("https://localhost" + OidcLogin.CallbackPath, query["redirect_uri"]);
            nonce = query["nonce"];
            return await client.PostAsync(OidcLogin.CallbackPath, new FormUrlEncodedContent(
                new Dictionary<string, string> { ["code"] = "authorization-code", ["state"] = query["state"]! }));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            TokenRequest = await request.Content!.ReadAsStringAsync(cancellationToken);
            var idToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = ClientId,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = "subject-1",
                    ["preferred_username"] = "alice",
                    ["nonce"] = nonce!,
                    ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles }
                },
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    access_token = "access-token",
                    token_type = "Bearer",
                    id_token = idToken,
                    expires_in = 300
                })
            };
        }
    }
}
