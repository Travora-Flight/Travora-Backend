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

        // Calculate the current local time of the airport using its GMT offset
        double offsetHours = 0;
        if (!string.IsNullOrWhiteSpace(airport.GMT))
        {
            double.TryParse(airport.GMT, out offsetHours);
        }
        var localTime = DateTime.UtcNow.AddHours(offsetHours);

        var departuresTask = FetchTimetableFlightsAsync(iataCode, "departure", localTime);
        var arrivalsTask = FetchTimetableFlightsAsync(iataCode, "arrival", localTime);

        await Task.WhenAll(departuresTask, arrivalsTask);

        var departures = departuresTask.Result;
        var arrivals = arrivalsTask.Result;

        var flights = departures.Concat(arrivals).OrderBy(f => f.ScheduledTime).ToList();

        // 2) Enrich flights with matching airline logos and city names from local DB
        if (flights.Any())
        {
            // 2a) Enrich flights with matching airline logos
            var airlineIatas = flights.Select(f => f.AirlineIata).Where(code => !string.IsNullOrEmpty(code)).Distinct().ToList();
            if (airlineIatas.Any())
            {
                var airlinesList = await _db.Airlines
                    .Where(a => airlineIatas.Contains(a.CodeIataAirline))
                    .ToListAsync();

                var airlinesDict = new Dictionary<string, Airline>(StringComparer.OrdinalIgnoreCase);
                foreach (var airline in airlinesList)
                {
                    if (!string.IsNullOrEmpty(airline.CodeIataAirline))
                    {
                        airlinesDict.TryAdd(airline.CodeIataAirline, airline);
                    }
                }

                foreach (var f in flights)
                {
                    if (airlinesDict.TryGetValue(f.AirlineIata, out var matchedAirline) && !string.IsNullOrEmpty(matchedAirline.LogoUrl))
                    {
                        f.AirlineLogoUrl = matchedAirline.LogoUrl;
                    }
                    else if (!string.IsNullOrEmpty(f.AirlineIata))
                    {
                        f.AirlineLogoUrl = $"https://pics.avs.io/200/200/{f.AirlineIata.ToUpper()}@2x.png";
                    }
                }
            }

            // 2b) Enrich flights with destination city names
            var destinationIatas = flights.Select(f => f.Destination).Where(code => !string.IsNullOrEmpty(code)).Distinct().ToList();
            if (destinationIatas.Any())
            {
                var airportsList = await _db.Airports
                    .Include(a => a.City)
                    .Where(a => destinationIatas.Contains(a.CodeIataAirport))
                    .ToListAsync();

                var airportsDict = new Dictionary<string, Airport>(StringComparer.OrdinalIgnoreCase);
                foreach (var airportItem in airportsList)
                {
                    if (!string.IsNullOrEmpty(airportItem.CodeIataAirport))
                    {
                        airportsDict.TryAdd(airportItem.CodeIataAirport, airportItem);
                    }
                }

                foreach (var f in flights)
                {
                    if (airportsDict.TryGetValue(f.Destination, out var matchedAirport))
                    {
                        f.City = matchedAirport.City != null
                            ? $"{matchedAirport.City.NameCity}, {matchedAirport.CodeIso2Country}"
                            : matchedAirport.CodeIso2Country;
                    }
                    else
                    {
                        // Fallback if not found in db, just use Destination code
                        f.City = f.Destination;
                    }
                }
            }
        }

        // Cache in Redis (5 min TTL)
        try
        {
            var json = JsonSerializer.Serialize(flights, JsonOptions);
            await _redis.SetAsync(cacheKey, json, TimetableTtl);
        }
        catch { /* Redis down — continue */ }

        return (flights, flights.Count);
    }

    private async Task<List<AirportFlightDto>> FetchTimetableFlightsAsync(string iataCode, string type, DateTime localTime)
    {
        var flightsList = new List<AirportFlightDto>();
        try
        {
            var client = _httpClientFactory.CreateClient("AviationEdge");
            var url = $"{_baseUrl}/timetable?key={_apiKey}&iataCode={Uri.EscapeDataString(iataCode)}&type={type}";
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (!json.TrimStart().StartsWith("{"))
                {
                    var rawFlights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);
                    if (rawFlights != null)
                    {
                        foreach (var f in rawFlights)
                        {
                            try
                            {
                                string schedTimeStr = type == "departure" 
                                    ? GetNestedString(f, "departure", "scheduledTime")
                                    : GetNestedString(f, "arrival", "scheduledTime");

                                if (DateTime.TryParse(schedTimeStr, out var schedDt))
                                {
                                    // Filter out flights that scheduled more than 30 minutes ago
                                    if (schedDt < localTime.AddMinutes(-30))
                                        continue;
                                }

                                string? actualTime = type == "departure"
                                    ? GetNestedStringOrNull(f, "departure", "actualTime")
                                    : GetNestedStringOrNull(f, "arrival", "actualTime");

                                string? estimatedTime = type == "departure"
                                    ? GetNestedStringOrNull(f, "departure", "estimatedTime")
                                    : GetNestedStringOrNull(f, "arrival", "estimatedTime");

                                string gate = type == "departure"
                                    ? GetNestedStringOrNull(f, "departure", "gate") ?? "—"
                                    : GetNestedStringOrNull(f, "arrival", "gate") ?? "—";

                                string? terminal = type == "departure"
                                    ? GetNestedStringOrNull(f, "departure", "terminal")
                                    : GetNestedStringOrNull(f, "arrival", "terminal");

                                string delay = type == "departure"
                                    ? GetNestedStringOrNull(f, "departure", "delay") ?? ""
                                    : GetNestedStringOrNull(f, "arrival", "delay") ?? "";

                                string destination = type == "departure"
                                    ? GetNestedString(f, "arrival", "iataCode")
                                    : GetNestedString(f, "departure", "iataCode");

                                flightsList.Add(new AirportFlightDto
                                {
                                    Destination = destination,
                                    FlightNumber = GetNestedString(f, "flight", "iataNumber"),
                                    ScheduledTime = ParseTime(schedTimeStr),
                                    EstimatedTime = ParseTime(estimatedTime),
                                    ActualTime = ParseTime(actualTime),
                                    Time = ParseTime(actualTime ?? estimatedTime ?? schedTimeStr),
                                    Gate = gate,
                                    Terminal = terminal,
                                    Type = type == "departure" ? "Departure" : "Arrival",
                                    Status = MapStatus(GetString(f, "status")),
                                    Delay = FormatDelay(delay),
                                    AirlineName = GetNestedString(f, "airline", "name"),
                                    AirlineIata = GetNestedString(f, "airline", "iataCode")
                                });

                                if (flightsList.Count >= 40)
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
            Console.WriteLine($"[AirportDetails] ❌ Timetable {type} API error: {ex.Message}");
        }

        return flightsList;
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
