using Travora.Application.DTOs.Airports;

namespace Travora.Application.Interfaces.External.Weather;

public interface IWeatherCache
{
    Task SetAsync(string icaoCode, WeatherDto data, int ttlMinutes);
    Task<WeatherDto?> GetAsync(string icaoCode);
}
