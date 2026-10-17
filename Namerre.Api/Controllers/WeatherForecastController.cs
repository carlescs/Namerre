using CMediator.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Namerre.Application.WeatherForecasts;

namespace Namerre.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class WeatherForecastController(ISender sender) : ControllerBase
{
    [HttpGet(Name = "GetWeatherForecast")]
    public Task<IReadOnlyList<WeatherForecast>> Get(CancellationToken cancellationToken)
        => sender.Send(new GetWeatherForecastQuery(), cancellationToken);
}
