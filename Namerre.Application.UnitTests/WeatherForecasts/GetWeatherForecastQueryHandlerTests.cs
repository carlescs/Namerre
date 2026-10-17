using Namerre.Application.WeatherForecasts;
using Xunit;

namespace Namerre.Application.UnitTests.WeatherForecasts;

public sealed class GetWeatherForecastQueryHandlerTests
{
    [Fact]
    public async Task HandleReturnsFiveForecastsForTheNextFiveDays()
    {
        var handler = new GetWeatherForecastQueryHandler();
        using var cancellation = new CancellationTokenSource();
        var dateBeforeHandling = DateOnly.FromDateTime(DateTime.Now);

        var forecasts = await handler.Handle(new GetWeatherForecastQuery(), cancellation.Token);

        var dateAfterHandling = DateOnly.FromDateTime(DateTime.Now);
        Assert.Equal(5, forecasts.Count);
        for (var index = 0; index < forecasts.Count; index++)
        {
            Assert.InRange(
                forecasts[index].Date,
                dateBeforeHandling.AddDays(index + 1),
                dateAfterHandling.AddDays(index + 1));
        }
    }

    [Fact]
    public async Task HandleReturnsTemperaturesAndSummariesWithinTheSupportedValues()
    {
        var handler = new GetWeatherForecastQueryHandler();
        string[] summaries =
        [
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        ];

        var forecasts = await handler.Handle(new GetWeatherForecastQuery(), CancellationToken.None);

        Assert.All(forecasts, forecast =>
        {
            Assert.InRange(forecast.TemperatureC, -20, 54);
            Assert.Contains(forecast.Summary, summaries);
        });
    }

    [Fact]
    public async Task HandleRejectsAnAlreadyCanceledToken()
    {
        var handler = new GetWeatherForecastQueryHandler();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.Handle(new GetWeatherForecastQuery(), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }
}
