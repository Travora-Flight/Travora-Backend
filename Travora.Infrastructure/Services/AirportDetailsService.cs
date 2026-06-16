using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Travora.Application.DTOs.Airports;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.External.Weather;
using Travora.Application.Interfaces.Services;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.Services;

public class AirportDetailsService : IAirportDetailsService
{
    private readonly ApplicationDbContext _db;
    private readonly IWeatherService _weatherApi;
    private readonly IWeatherCache _weatherCache;
    private readonly IUpstashRedisService _redis;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly int _cacheTtlMinutes;

    private static readonly TimeSpan TimetableTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AirportDetailsService(
        ApplicationDbContext db,
        IWeatherService weatherApi,
        IWeatherCache weatherCache,
        IUpstashRedisService redis,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _db = db;
        _weatherApi = weatherApi;
        _weatherCache = weatherCache;
        _redis = redis;
        _httpClientFactory = httpClientFactory;
        _baseUrl = configuration["AviationEdge:BaseUrl"] ?? "https://aviation-edge.com/v2/public";
        _apiKey = configuration["AviationEdge:ApiKey"] ?? "";
        _cacheTtlMinutes = configuration.GetValue<int>("WeatherApi:CacheTtlMinutes", 30);
    }

    public async Task<AirportDetailsResponse> GetAirportDetailsAsync(string code)
    {
        // 1) Find airport by ICAO or IATA code
        var airport = await _db.Airports
            .Include(a => a.City)
            .FirstOrDefaultAsync(a => a.CodeIcaoAirport == code || a.CodeIataAirport == code);

        if (airport == null)
            throw new KeyNotFoundException("Airport not found");

        // 2) Get weather (Aviation Weather API requires ICAO code)
        var weather = await GetWeatherAsync(airport.CodeIcaoAirport, airport);

        // 3) Get flights from Aviation Edge timetable (requires IATA code)
        var (flights, totalFlights) = await GetTodayFlightsAsync(airport);

        // 4) Build response
        return new AirportDetailsResponse
        {
            AirportName = airport.NameAirport,
            City = airport.City != null
                ? $"{airport.City.NameCity}, {airport.CodeIso2Country}"
                : airport.CodeIso2Country,
            IataCode = airport.CodeIataAirport,
            IcaoCode = airport.CodeIcaoAirport,
            Location = airport.City != null
                ? $"{airport.City.NameCity}, {airport.CodeIso2Country}"
                : airport.CodeIso2Country,
            TimeZone = FormatGmt(airport.GMT),
            Weather = weather,
            TotalFlights = totalFlights,
            Flights = flights
        };
    }

    private async Task<WeatherDto?> GetWeatherAsync(string icaoCode, Airport airport)
    {
        // Check Redis cache first
        var cached = await _weatherCache.GetAsync(icaoCode);
        if (cached != null)
            return cached;

        // Query by IATA as primary, fallback to METAR style with ICAO
        var query = !string.IsNullOrWhiteSpace(airport.CodeIataAirport)
            ? $"iata:{airport.CodeIataAirport}"
            : $"metar:{airport.CodeIcaoAirport}";

        // Fetch from Weather API
        var weather = await _weatherApi.GetWeatherAsync(query);
        if (weather == null)
            return null;

        // Cache in Redis
        await _weatherCache.SetAsync(icaoCode, weather, _cacheTtlMinutes);

        return weather;
    }

    // ========================================================
    // Aviation Edge /timetable — departure + arrival
    // ========================================================
    private async Task<(List<AirportFlightDto> Flights, int Total)> GetTodayFlightsAsync(Airport airport)
    {
        var iataCode = airport.CodeIataAirport;
        var cacheKey = $"timetable:airport:{iataCode}";

        // 1) Check Redis cache
        try
        {
            var cached = await _redis.GetAsync(cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var cachedFlights = JsonSerializer.Deserialize<List<AirportFlightDto>>(cached, JsonOptions);
                if (cachedFlights != null)
                    return (cachedFlights, cachedFlights.Count);
            }
        }
        catch { /* Redis down — continue */ }

        var flights = new List<AirportFlightDto>();
        
        // Calculate the current local time of the airport using its GMT offset
        double offsetHours = 0;
        if (!string.IsNullOrWhiteSpace(airport.GMT))
        {
            double.TryParse(airport.GMT, out offsetHours);
        }
        var localTime = DateTime.UtcNow.AddHours(offsetHours);

        try
        {
            var client = _httpClientFactory.CreateClient("AviationEdge");

            // Fetch departures
            var depUrl = $"{_baseUrl}/timetable?key={_apiKey}&iataCode={Uri.EscapeDataString(iataCode)}&type=departure";
            var depResponse = await client.GetAsync(depUrl);

            if (depResponse.IsSuccessStatusCode)
            {
                var depJson = await depResponse.Content.ReadAsStringAsync();
                if (!depJson.TrimStart().StartsWith("{"))
                {
                    var depFlights = JsonSerializer.Deserialize<List<JsonElement>>(depJson, JsonOptions);
                    if (depFlights != null)
                    {
                        foreach (var f in depFlights)
                        {
                            try
                            {
                                var schedTimeStr = GetNestedString(f, "departure", "scheduledTime");
                                if (DateTime.TryParse(schedTimeStr, out var schedDt))
                                {
                                    // Filter out flights that scheduled more than 30 minutes ago
                                    if (schedDt < localTime.AddMinutes(-30))
                                        continue;
                                }

                                var depActual = GetNestedStringOrNull(f, "departure", "actualTime");
                                var depEstimated = GetNestedStringOrNull(f, "departure", "estimatedTime");

                                flights.Add(new AirportFlightDto
                                {
                                    Destination = GetNestedString(f, "arrival", "iataCode"),
                                    FlightNumber = GetNestedString(f, "flight", "iataNumber"),
                                    ScheduledTime = ParseTime(schedTimeStr),
                                    Time = ParseTime(depActual ?? depEstimated ?? schedTimeStr),
                                    Gate = GetNestedStringOrNull(f, "departure", "gate")
                                        ?? (GetNestedStringOrNull(f, "departure", "terminal") is string depTerm ? $"T{depTerm}" : "—"),
                                    Type = "Departure",
                                    Status = MapStatus(GetString(f, "status")),
                                    Delay = FormatDelay(GetNestedStringOrNull(f, "departure", "delay"))
                                });

                                if (flights.Count(fl => fl.Type == "Departure") >= 40)
                                    break;
                            }
                            catch { /* Skip malformed */ }
                        }
                    }
                }
            }

            // Fetch arrivals
            var arrUrl = $"{_baseUrl}/timetable?key={_apiKey}&iataCode={Uri.EscapeDataString(iataCode)}&type=arrival";
            var arrResponse = await client.GetAsync(arrUrl);

            if (arrResponse.IsSuccessStatusCode)
            {
                var arrJson = await arrResponse.Content.ReadAsStringAsync();
                if (!arrJson.TrimStart().StartsWith("{"))
                {
                    var arrFlights = JsonSerializer.Deserialize<List<JsonElement>>(arrJson, JsonOptions);
                    if (arrFlights != null)
                    {
                        foreach (var f in arrFlights)
                        {
                            try
                            {
                                var schedTimeStr = GetNestedString(f, "arrival", "scheduledTime");
                                if (DateTime.TryParse(schedTimeStr, out var schedDt))
                                {
                                    // Filter out flights that scheduled more than 30 minutes ago
                                    if (schedDt < localTime.AddMinutes(-30))
                                        continue;
                                }

                                var arrActual = GetNestedStringOrNull(f, "arrival", "actualTime");
                                var arrEstimated = GetNestedStringOrNull(f, "arrival", "estimatedTime");

                                flights.Add(new AirportFlightDto
                                {
                                    Destination = GetNestedString(f, "departure", "iataCode"),
                                    FlightNumber = GetNestedString(f, "flight", "iataNumber"),
                                    ScheduledTime = ParseTime(schedTimeStr),
                                    Time = ParseTime(arrActual ?? arrEstimated ?? schedTimeStr),
                                    Gate = GetNestedStringOrNull(f, "arrival", "gate")
                                        ?? (GetNestedStringOrNull(f, "arrival", "terminal") is string arrTerm ? $"T{arrTerm}" : "—"),
                                    Type = "Arrival",
                                    Status = MapStatus(GetString(f, "status")),
                                    Delay = FormatDelay(GetNestedStringOrNull(f, "arrival", "delay"))
                                });

                                if (flights.Count(fl => fl.Type == "Arrival") >= 40)
                                    break;
                            }
                            catch { /* Skip malformed */ }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AirportDetails] ❌ Timetable API error: {ex.Message}");
        }

        // Sort by scheduled time
        flights = flights.OrderBy(f => f.ScheduledTime).ToList();

        // Cache in Redis (5 min TTL)
        try
        {
            var json = JsonSerializer.Serialize(flights, JsonOptions);
            await _redis.SetAsync(cacheKey, json, TimetableTtl);
        }
        catch { /* Redis down — continue */ }

        return (flights, flights.Count);
    }

    // ========================================================
    // Helpers
    // ========================================================
    private static string ParseTime(string? scheduledTime)
    {
        if (string.IsNullOrWhiteSpace(scheduledTime)) return "";

        // Format: "2025-10-21T17:35:00.000" → "17:35"
        if (DateTime.TryParse(scheduledTime, out var dt))
            return dt.ToString("HH:mm");

        // Already in "HH:mm" format
        if (scheduledTime.Contains(':') && scheduledTime.Length <= 8)
            return scheduledTime;

        return scheduledTime;
    }

    private static string MapStatus(string status)
    {
        return status?.ToLower() switch
        {
            "landed" => "Landed",
            "scheduled" => "Scheduled",
            "cancelled" => "Cancelled",
            "active" => "Active",
            "incident" => "Incident",
            "diverted" => "Diverted",
            "redirected" => "Redirected",
            "unknown" => "Unknown",
            _ => string.IsNullOrWhiteSpace(status) ? "Unknown" : status
        };
    }

    private static string? FormatDelay(string? delay)
    {
        if (string.IsNullOrWhiteSpace(delay)) return null;
        if (int.TryParse(delay, out var minutes) && minutes > 0)
            return $"{minutes} min";
        return null;
    }

    private static string FormatGmt(string gmt)
    {
        if (string.IsNullOrWhiteSpace(gmt))
            return "GMT";

        gmt = gmt.Trim();

        if (gmt.StartsWith("-"))
            return $"GMT{gmt}";

        return $"GMT+{gmt}";
    }

    // ── JSON Navigation Helpers ──

    private static string GetNestedString(JsonElement element, string obj, string prop)
    {
        if (element.TryGetProperty(obj, out var nested) &&
            nested.TryGetProperty(prop, out var value))
        {
            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.ToString();
        }
        return "";
    }

    private static string? GetNestedStringOrNull(JsonElement element, string obj, string prop)
    {
        if (element.TryGetProperty(obj, out var nested) &&
            nested.TryGetProperty(prop, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString();
            return string.IsNullOrWhiteSpace(str) ? null : str;
        }
        return null;
    }

    private static string GetString(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value))
        {
            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.ToString();
        }
        return "";
    }
}
