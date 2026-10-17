using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class WeatherForecastApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetPreservesTheWeatherForecastContract()
    {
        using var client = CreateClient();

        var forecasts = await client.GetFromJsonAsync<JsonElement>("/api/WeatherForecast");

        Assert.Equal(JsonValueKind.Array, forecasts.ValueKind);
        Assert.Equal(5, forecasts.GetArrayLength());
        foreach (var forecast in forecasts.EnumerateArray())
        {
            Assert.Equal(4, forecast.EnumerateObject().Count());
            Assert.True(DateOnly.TryParse(forecast.GetProperty("date").GetString(), out _));
            var temperatureC = forecast.GetProperty("temperatureC").GetInt32();
            Assert.InRange(temperatureC, -20, 54);
            Assert.Equal(
                32 + (int)(temperatureC / 0.5556),
                forecast.GetProperty("temperatureF").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(forecast.GetProperty("summary").GetString()));
        }
    }

    [Theory]
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
