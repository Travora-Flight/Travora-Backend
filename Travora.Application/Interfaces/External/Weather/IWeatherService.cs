using Travora.Application.DTOs.Airports;

namespace Travora.Application.Interfaces.External.Weather;

public interface IWeatherService
{
    /// <summary>
    /// Fetches the current weather and 1-day forecast for the given query.
    /// Query can be "iata:XXX", "metar:XXXX", "lat,lon", or city name.
    /// </summary>
    Task<WeatherDto?> GetWeatherAsync(string query);
}
