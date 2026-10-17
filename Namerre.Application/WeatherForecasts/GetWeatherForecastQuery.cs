using CMediator.Abstractions;

namespace Namerre.Application.WeatherForecasts;

public sealed record GetWeatherForecastQuery : IRequest<IReadOnlyList<WeatherForecast>>;
