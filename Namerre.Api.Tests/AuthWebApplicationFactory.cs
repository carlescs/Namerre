using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Namerre.Authentication;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string Username = "test-admin";
    public const string Password = "test-password-not-for-production";
    private static readonly string PasswordHash = Passwords.HashPassword(Password);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:Authentication:Username"] = Username,
                ["Namerre:Authentication:PasswordHash"] = PasswordHash,
                ["Namerre:RootPath"] = "",
                ["Namerre:DataPath"] = ""
            }));

    public static HttpClient AnonymousClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    public static async Task<HttpClient> AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = AnonymousClient(factory);
        await RefreshCsrf(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { Username, Password });
        login.EnsureSuccessStatusCode();
        await RefreshCsrf(client);
        return client;
    }

    public static async Task RefreshCsrf(HttpClient client)
    {
        using var session = await client.GetAsync("/api/auth/session");
        Assert.True(session.IsSuccessStatusCode, await session.Content.ReadAsStringAsync());
        var cookie = session.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        var token = cookie.Split(';')[0]["XSRF-TOKEN=".Length..];
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(token));
    }
}
