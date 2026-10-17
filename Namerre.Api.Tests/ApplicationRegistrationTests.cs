using CMediator;
using CMediator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Namerre.Application;
using Namerre.Application.WeatherForecasts;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class ApplicationRegistrationTests
{
    [Fact]
    public async Task GeneratedRegistrationDispatchesToTheWeatherForecastHandler()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dispatcher = Assert.Single(scope.ServiceProvider.GetServices<IRequestDispatcher>());
        var request = new GetWeatherForecastQuery();

        Assert.True(dispatcher.CanDispatch(request));

        var forecasts = await sender.Send(request);

        Assert.Equal(5, forecasts.Count);
        Assert.All(forecasts, forecast =>
        {
            Assert.InRange(forecast.TemperatureC, -20, 54);
            Assert.False(string.IsNullOrWhiteSpace(forecast.Summary));
            Assert.Equal(32 + (int)(forecast.TemperatureC / 0.5556), forecast.TemperatureF);
        });
        for (var index = 1; index < forecasts.Count; index++)
        {
            Assert.Equal(forecasts[index - 1].Date.AddDays(1), forecasts[index].Date);
        }
    }

    [Fact]
    public async Task GeneratedDispatchPropagatesCancellation()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.Send(new GetWeatherForecastQuery(), cancellation.Token));
    }
}
