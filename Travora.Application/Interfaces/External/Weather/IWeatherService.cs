using Travora.Application.DTOs.Airports;
using Travora.Application.DTOs.Flights;

namespace Travora.Application.Interfaces.External.Weather;

public interface IWeatherService
{
    /// <summary>
    /// Fetches the current weather and 1-day forecast for the given query.
    /// Query can be "iata:XXX", "metar:XXXX", "lat,lon", or city name.
    /// </summary>
    Task<WeatherDto?> GetWeatherAsync(string query);

    /// <summary>
    /// Fetches the hourly weather forecast for a specific UTC date and time for predicting delay.
    /// </summary>
    Task<PredictionWeatherDto?> GetHourlyWeatherAsync(string query, DateTime dateTimeUtc);
}
