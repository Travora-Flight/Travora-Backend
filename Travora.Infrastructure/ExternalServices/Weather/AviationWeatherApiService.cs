using System.Text.Json;
using Microsoft.Extensions.Logging;
using Travora.Application.DTOs.Airports;
using Travora.Application.Interfaces.External.Weather;

namespace Travora.Infrastructure.ExternalServices.Weather;

public class AviationWeatherApiService : IAviationWeatherService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AviationWeatherApiService> _logger;

    public AviationWeatherApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<AviationWeatherApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<WeatherDto?> GetMetarAsync(string icaoCode)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("AviationWeather");
            var response = await client.GetAsync($"metar?ids={icaoCode}&format=json");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Aviation Weather API returned {StatusCode} for {IcaoCode}",
                    response.StatusCode, icaoCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var metarArray = JsonSerializer.Deserialize<List<MetarApiResponse>>(json, options);

            if (metarArray == null || metarArray.Count == 0)
            {
                _logger.LogWarning("Aviation Weather API returned empty array for {IcaoCode}", icaoCode);
                return null;
            }

            var latest = metarArray[0];

            return new WeatherDto
            {
                Temperature = latest.Temp ?? 0,
                Dewpoint = latest.Dewp ?? 0,
                WindDirection = latest.Wdir ?? 0,
                WindSpeed = latest.Wspd ?? 0,
                Visibility = latest.Visib ?? "N/A",
                Altimeter = latest.Altim ?? 0,
                CloudCover = latest.Cover ?? string.Empty,
                FlightCategory = latest.FltCat ?? string.Empty,
                MetarType = latest.MetarType ?? string.Empty,
                RawObservation = latest.RawOb ?? string.Empty,
                ReportTime = latest.ReportTime ?? DateTime.UtcNow,
                CloudLayers = latest.Clouds?.Select(c => new CloudLayerDto
                {
                    Cover = c.Cover ?? string.Empty,
                    Base = c.Base ?? 0
                }).ToList() ?? new List<CloudLayerDto>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching METAR data for {IcaoCode}", icaoCode);
            return null;
        }
    }

    // Internal classes for API response mapping
    private class MetarApiResponse
    {
        public decimal? Temp { get; set; }
        public decimal? Dewp { get; set; }
        public int? Wdir { get; set; }
        public decimal? Wspd { get; set; }
        public string? Visib { get; set; }
        public decimal? Altim { get; set; }
        public string? Cover { get; set; }
        public string? FltCat { get; set; }
        public string? MetarType { get; set; }
        public string? RawOb { get; set; }
        public int? Elev { get; set; }
        public DateTime? ReportTime { get; set; }
        public DateTime? ReceiptTime { get; set; }
        public List<MetarCloudLayer>? Clouds { get; set; }
    }

    private class MetarCloudLayer
    {
        public string? Cover { get; set; }
        public int? Base { get; set; }
    }
}
