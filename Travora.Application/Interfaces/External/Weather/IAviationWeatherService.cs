using Travora.Application.DTOs.Airports;

namespace Travora.Application.Interfaces.External.Weather;

public interface IAviationWeatherService
{
    Task<WeatherDto?> GetMetarAsync(string icaoCode);
}
