using Namerre.Application.WeatherForecasts;
using Xunit;

namespace Namerre.Application.UnitTests.WeatherForecasts;

public sealed class WeatherForecastTests
{
    [Theory]
    [InlineData(-20, -3)]
    [InlineData(0, 32)]
    [InlineData(20, 67)]
    [InlineData(54, 129)]
    public void TemperatureFUsesTheExistingTruncatedConversion(int temperatureC, int expectedTemperatureF)
    {
        var forecast = new WeatherForecast { TemperatureC = temperatureC };

        Assert.Equal(expectedTemperatureF, forecast.TemperatureF);
    }
}
