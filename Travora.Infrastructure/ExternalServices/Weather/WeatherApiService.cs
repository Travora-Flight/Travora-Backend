using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Travora.Application.DTOs.Airports;
using Travora.Application.DTOs.Flights;
using Travora.Application.Interfaces.External.Weather;

namespace Travora.Infrastructure.ExternalServices.Weather;

public class WeatherApiService : IWeatherService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WeatherApiService> _logger;
    private readonly string _apiKey;

    public WeatherApiService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WeatherApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["WeatherApi:ApiKey"] ?? "ca94944dc7174eff966184802261404";
    }

    public async Task<WeatherDto?> GetWeatherAsync(string query)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("WeatherApi");
            // Fetch 1 day of forecast to get sunrise, sunset, max/min temp, and chance of rain
            var url = $"forecast.json?key={_apiKey}&q={Uri.EscapeDataString(query)}&days=1&aqi=no&alerts=no";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WeatherAPI returned status code {StatusCode} for query {Query}", 
                    response.StatusCode, query);
                return null;
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<WeatherApiResponse>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse == null || apiResponse.Current == null)
            {
                _logger.LogWarning("Failed to deserialize WeatherAPI response for query {Query}", query);
                return null;
            }

            var current = apiResponse.Current;
            var condition = current.Condition ?? new ConditionResponse();
            
            // Get today's forecast details (maxtemp, mintemp, sunrise, sunset, chance of rain)
            var forecastday = apiResponse.Forecast?.Forecastday?.FirstOrDefault();
            var day = forecastday?.Day;
            var astro = forecastday?.Astro;

            // Report time parsing
            DateTime reportTime = DateTime.UtcNow;
            if (DateTime.TryParse(current.Last_Updated, out var parsedReportTime))
            {
                reportTime = parsedReportTime;
            }

            return new WeatherDto
            {
                Temperature = current.Temp_C,
                FeelsLike = current.Feelslike_C,
                WindDirection = current.Wind_Degree,
                WindSpeed = current.Wind_Kph,
                Visibility = current.Vis_Km.ToString(),
                Pressure = current.Pressure_Mb,
                Humidity = current.Humidity,
                
                ConditionText = condition.Text,
                ConditionIcon = condition.Icon.StartsWith("//") ? $"https:{condition.Icon}" : condition.Icon,
                ConditionCode = condition.Code,
                
                Sunrise = astro?.Sunrise ?? string.Empty,
                Sunset = astro?.Sunset ?? string.Empty,
                ChanceOfRain = day?.Daily_Chance_Of_Rain ?? 0,
                MaxTemp = day?.Maxtemp_C ?? 0,
                MinTemp = day?.Mintemp_C ?? 0,
                
                ReportTime = reportTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching weather data from WeatherAPI for query {Query}", query);
            return null;
        }
    }

    public async Task<PredictionWeatherDto?> GetHourlyWeatherAsync(string query, DateTime dateTimeUtc)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("WeatherApi");
            var dateStr = dateTimeUtc.ToString("yyyy-MM-dd");
            var url = $"forecast.json?key={_apiKey}&q={Uri.EscapeDataString(query)}&dt={dateStr}&aqi=no&alerts=no";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WeatherAPI returned status code {StatusCode} for hourly query {Query} and date {Date}", 
                    response.StatusCode, query, dateStr);
                return null;
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<WeatherApiResponse>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var forecastday = apiResponse?.Forecast?.Forecastday?.FirstOrDefault();
            if (forecastday == null || forecastday.Hour == null || !forecastday.Hour.Any())
            {
                _logger.LogWarning("No hourly forecast details found in WeatherAPI response for query {Query} and date {Date}", query, dateStr);
                return null;
            }

            // Find the hour that matches dateTimeUtc hour closest
            var targetHour = dateTimeUtc.Hour;
            var hourData = forecastday.Hour.FirstOrDefault(h => 
            {
                if (DateTime.TryParse(h.Time, out var parsedTime))
                {
                    return parsedTime.Hour == targetHour;
                }
                return false;
            }) ?? forecastday.Hour.OrderBy(h => 
            {
                if (DateTime.TryParse(h.Time, out var parsedTime))
                {
                    return Math.Abs((parsedTime - dateTimeUtc).TotalMinutes);
                }
                return double.MaxValue;
            }).FirstOrDefault();

            if (hourData == null)
            {
                return null;
            }

            return new PredictionWeatherDto
            {
                TempF = (double)hourData.Temp_F,
                WindChillF = (double)hourData.Feelslike_F, // feelslike_f or windchill_f
                Humidity = hourData.Humidity,
                WindspeedKmph = (double)hourData.Wind_Kph,
                WindGustKmph = (double)hourData.Gust_Kph,
                WinddirDegree = hourData.Wind_Degree,
                WeatherCode = hourData.Condition?.Code ?? 0,
                PrecipMM = (double)hourData.Precip_Mm,
                Visibility = (double)hourData.Vis_Km,
                Pressure = (double)hourData.Pressure_Mb,
                Cloudcover = hourData.Cloud,
                DewPointF = (double)hourData.Dewpoint_F
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching hourly weather data from WeatherAPI for query {Query}", query);
            return null;
        }
    }

    // JSON response mapping classes
    private class WeatherApiResponse
    {
        public CurrentResponse Current { get; set; } = null!;
        public ForecastResponse Forecast { get; set; } = null!;
    }

    private class CurrentResponse
    {
        public decimal Temp_C { get; set; }
        public decimal Feelslike_C { get; set; }
        public int Wind_Degree { get; set; }
        public decimal Wind_Kph { get; set; }
        public decimal Vis_Km { get; set; }
        public decimal Pressure_Mb { get; set; }
        public int Humidity { get; set; }
        public string Last_Updated { get; set; } = string.Empty;
        public ConditionResponse Condition { get; set; } = null!;
    }

    private class ConditionResponse
    {
        public string Text { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public int Code { get; set; }
    }

    private class ForecastResponse
    {
        public List<ForecastDayResponse> Forecastday { get; set; } = new();
    }

    private class ForecastDayResponse
    {
        public DayResponse Day { get; set; } = null!;
        public AstroResponse Astro { get; set; } = null!;
        public List<HourResponse> Hour { get; set; } = new();
    }

    private class DayResponse
    {
        public decimal Maxtemp_C { get; set; }
        public decimal Mintemp_C { get; set; }
        public int Daily_Chance_Of_Rain { get; set; }
    }

    private class AstroResponse
    {
        public string Sunrise { get; set; } = string.Empty;
        public string Sunset { get; set; } = string.Empty;
    }

    private class HourResponse
    {
        public string Time { get; set; } = string.Empty;
        public decimal Temp_F { get; set; }
        public decimal Feelslike_F { get; set; }
        public decimal Windchill_f { get; set; }
        public int Humidity { get; set; }
        public decimal Wind_Kph { get; set; }
        public decimal Gust_Kph { get; set; }
        public int Wind_Degree { get; set; }
        public ConditionResponse Condition { get; set; } = null!;
        public decimal Precip_Mm { get; set; }
        public decimal Vis_Km { get; set; }
        public decimal Pressure_Mb { get; set; }
        public int Cloud { get; set; }
        public decimal Dewpoint_F { get; set; }
    }
}
