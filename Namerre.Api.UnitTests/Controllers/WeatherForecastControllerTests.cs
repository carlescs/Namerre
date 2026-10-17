using CMediator.Abstractions;
using Namerre.Application.WeatherForecasts;
using Namerre.Controllers;
using NSubstitute;
using Xunit;

namespace Namerre.Api.UnitTests.Controllers;

public sealed class WeatherForecastControllerTests
{
    [Fact]
    public async Task GetSendsTheQueryWithTheRequestCancellationTokenAndReturnsItsResponse()
    {
        var sender = Substitute.For<ISender>();
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<WeatherForecast> forecasts =
        [
            new() { Date = new DateOnly(2026, 1, 1), TemperatureC = 20, Summary = "Mild" }
        ];
        sender.Send(Arg.Any<GetWeatherForecastQuery>(), cancellation.Token)
            .Returns(Task.FromResult(forecasts));
        var controller = new WeatherForecastController(sender);

        var result = await controller.Get(cancellation.Token);

        Assert.Same(forecasts, result);
        await sender.Received(1).Send(Arg.Any<GetWeatherForecastQuery>(), cancellation.Token);
    }

    [Fact]
    public async Task GetPropagatesMediatorFailures()
    {
        var sender = Substitute.For<ISender>();
        var failure = new InvalidOperationException("Dispatch failed.");
        sender.Send(Arg.Any<GetWeatherForecastQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<WeatherForecast>>(failure));
        var controller = new WeatherForecastController(sender);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Get(CancellationToken.None));

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task GetPropagatesMediatorCancellation()
    {
        var sender = Substitute.For<ISender>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        sender.Send(Arg.Any<GetWeatherForecastQuery>(), cancellation.Token)
            .Returns(Task.FromCanceled<IReadOnlyList<WeatherForecast>>(cancellation.Token));
        var controller = new WeatherForecastController(sender);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.Get(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }
}
