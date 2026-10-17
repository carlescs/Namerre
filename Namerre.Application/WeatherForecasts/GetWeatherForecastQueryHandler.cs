using CMediator.Abstractions;

namespace Namerre.Application.WeatherForecasts;

internal sealed class GetWeatherForecastQueryHandler
    : IRequestHandler<GetWeatherForecastQuery, IReadOnlyList<WeatherForecast>>
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    public Task<IReadOnlyList<WeatherForecast>> Handle(
        GetWeatherForecastQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<WeatherForecast> forecasts = Enumerable.Range(1, 5)
            .Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();

        return Task.FromResult(forecasts);
    }
}
