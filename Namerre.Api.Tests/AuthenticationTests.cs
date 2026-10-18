using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData("GET", "/api/folders")]
    [InlineData("GET", "/api/folders/selection")]
    [InlineData("GET", "/api/rename-plugins")]
    [InlineData("GET", "/api/rename-runs")]
    [InlineData("GET", "/api/rename-plans/anything")]
    [InlineData("POST", "/api/rename-previews")]
    [InlineData("POST", "/api/rename-plans")]
    [InlineData("POST", "/api/rename-plans/anything/execute")]
    [InlineData("POST", "/api/rename-runs/anything/rollback")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("GET", "/openapi/v1.json")]
    public async Task AnonymousRequestsCannotAccessProtectedEndpoints(string method, string path)
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task LoginSessionAndLogoutUseSecureCookiesAndDoNotCacheData()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        var anonymous = await client.GetFromJsonAsync<Session>("/api/auth/session");
        Assert.Null(anonymous!.Username);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new { AuthWebApplicationFactory.Username, AuthWebApplicationFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-namerre-session=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.DoesNotContain("expires=", cookie);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var session = await client.GetAsync("/api/auth/session");
        Assert.Equal(AuthWebApplicationFactory.Username,
            (await session.Content.ReadFromJsonAsync<Session>())!.Username);
        Assert.True(session.Headers.CacheControl!.NoStore);
        using var catalog = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        using var logout = await client.PostAsJsonAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var denied = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task LoginAndAuthenticatedMutationsRequireCsrfTokens()
    {
        using var factory = new AuthWebApplicationFactory();
        using var anonymous = AuthWebApplicationFactory.AnonymousClient(factory);
        using var login = await anonymous.PostAsJsonAsync("/api/auth/login",
            new { AuthWebApplicationFactory.Username, AuthWebApplicationFactory.Password });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);

        using var client = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        using var missing = await client.PostAsJsonAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "forged");
        using var invalid = await client.PostAsJsonAsync("/api/rename-previews", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var stillAuthenticated = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.OK, stillAuthenticated.StatusCode);
    }

    [Fact]
    public async Task InvalidCredentialsHaveGenericErrorsAndSharedRateLimit()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var invalid = await client.PostAsJsonAsync("/api/auth/login",
                new { username = attempt % 2 == 0 ? "unknown" : AuthWebApplicationFactory.Username, password = "incorrect" });
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
            Assert.Contains("Invalid username or password.", await invalid.Content.ReadAsStringAsync());
        }
        using var otherClient = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(otherClient);
        using var limited = await otherClient.PostAsJsonAsync("/api/auth/login",
            new { AuthWebApplicationFactory.Username, AuthWebApplicationFactory.Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredOrPreviousCredentialSessionsAreRejected(bool expired)
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, AuthWebApplicationFactory.Username),
            new Claim("credential-stamp", factory.Services.GetRequiredService<Namerre.Authentication.AdminAccount>().Stamp)
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        if (!expired)
            ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst("credential-stamp")!);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddHours(-9),
            ExpiresUtc = expired ? DateTimeOffset.UtcNow.AddHours(-1) : DateTimeOffset.UtcNow.AddHours(1)
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        client.DefaultRequestHeaders.Add("Cookie", "__Host-namerre-session=" + options.TicketDataFormat.Protect(ticket));
        using var response = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-password-hash")]
    public void MissingOrInvalidConfigurationPreventsStartup(string hash)
    {
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:PasswordHash"] = hash })));
        var error = Assert.Throws<InvalidOperationException>(() => { _ = factory.Server; });
        Assert.Contains("Authentication:PasswordHash", error.Message);
    }

    [Fact]
    public async Task PlainHttpCannotEstablishASession()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        client.BaseAddress = new Uri("http://localhost");
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { AuthWebApplicationFactory.Username, AuthWebApplicationFactory.Password });
        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    private sealed record Session(string? Username);
}
