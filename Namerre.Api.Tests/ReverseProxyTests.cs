using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class ReverseProxyTests
{
    [Theory]
    [InlineData("KnownProxies", "10.0.0.2")]
    [InlineData("KnownNetworks", "10.0.0.0/24")]
    public async Task TrustedProxyCanUseHttpUpstreamForHttpsBrowserSessions(string setting, string value)
    {
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"Namerre:ReverseProxy:{setting}:0"] = value
                }));
            builder.ConfigureServices(services =>
                services.AddTransient<IStartupFilter>(_ => new HttpUpstreamStartupFilter()));
        });
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.1");

        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new { AuthWebApplicationFactory.Username, AuthWebApplicationFactory.Password });

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Null(login.Headers.Location);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-namerre-session=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var protectedResponse = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
        using var logout = await client.PostAsJsonAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var denied = await client.GetAsync("/api/rename-plugins");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("10.0.0.3", "https")]
    [InlineData("10.0.0.2", null)]
    [InlineData("10.0.0.2", "http")]
    public async Task HttpWithoutTrustedForwardedHttpsCannotEstablishASession(
        string? trustedProxy, string? forwardedProto)
    {
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Namerre:ReverseProxy:KnownProxies:0"] = trustedProxy ?? "10.0.0.3"
                }));
            builder.ConfigureServices(services =>
                services.AddTransient<IStartupFilter>(_ => new HttpUpstreamStartupFilter()));
        });
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        if (forwardedProto is not null)
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", forwardedProto);

        using var response = await client.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("HTTPS is required.", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    private sealed class HttpUpstreamStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Request.Scheme = "http";
                    context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
                    return nextMiddleware(context);
                });
                next(app);
            };
    }
}
