using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class ApiHostingTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Theory]
    [InlineData("/api/WeatherForecast")]
    [InlineData("/api/weatherforecast/")]
    [InlineData("/api/unknown")]
    [InlineData("/api/unknown.json")]
    [InlineData("/missing.css")]
    public async Task MissingApiRoutesAndAssetsRemainNotFound(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/files")]
    public async Task FrontendRoutesStillServeAngular(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<app-root", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/auth/session")]
    [InlineData("/api/unknown")]
    public async Task ResponsesCarryBrowserSecurityHeaders(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Opener-Policy")));
        Assert.Contains("camera=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")));
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.StartsWith("default-src 'self'; script-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
    }

    [Fact]
    public async Task ContentSecurityPolicyAllowsEveryInlineScriptOfTheServedIndex()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        // The Angular build adds its own inline loader script next to the theme script in index.html.
        var inline = Regex.Matches(html, "<script>(.*?)</script>", RegexOptions.Singleline);
        Assert.NotEmpty(inline);
        foreach (Match script in inline)
        {
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(
                script.Groups[1].Value.Replace("\r\n", "\n"))));
            Assert.Contains($"'sha256-{hash}'", policy);
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/files")]
    [InlineData("/index.html")]
    [InlineData("/manifest.webmanifest")]
    [InlineData("/ngsw-worker.js")]
    [InlineData("/ngsw.json")]
    public async Task UnhashedShellFilesAreRevalidated(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache, $"{path} should be served with Cache-Control: no-cache.");
    }

    [Fact]
    public async Task WebAppManifestIsServedForInstallation()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/manifest.webmanifest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType?.MediaType);
        using var manifest = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("standalone", manifest.RootElement.GetProperty("display").GetString());
        Assert.Equal("/", manifest.RootElement.GetProperty("start_url").GetString());
    }

    [Fact]
    public async Task HashedBundlesKeepDefaultCaching()
    {
        using var client = CreateClient();
        using var index = await client.GetAsync("/");
        var bundle = Regex.Match(await index.Content.ReadAsStringAsync(), @"src=""(main-[^""]+\.js)""").Groups[1].Value;
        Assert.NotEmpty(bundle);

        using var response = await client.GetAsync("/" + bundle);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.CacheControl);
    }

    private HttpClient CreateClient()
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
}
