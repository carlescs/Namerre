using System.Net;
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

    private HttpClient CreateClient()
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
}
