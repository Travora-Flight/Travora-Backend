using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Travora.Application.DTOs.Flights.Tracker;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.Services;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.Services;

public class FlightTrackerService : IFlightTrackerService
{
    private readonly IUpstashRedisService _redis;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationDbContext _db;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    private const string LiveFlightsCacheKey = "flights:live:all";
    private const string TimetableCachePrefix = "timetable:";
    private static readonly TimeSpan LiveFlightsTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TimetableTtl = TimeSpan.FromMinutes(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FlightTrackerService(
        IUpstashRedisService redis,
        IHttpClientFactory httpClientFactory,
        ApplicationDbContext db,
        IConfiguration configuration)
    {
        _redis = redis;
        _httpClientFactory = httpClientFactory;
        _db = db;
        _baseUrl = configuration["AviationEdge:BaseUrl"] ?? "https://aviation-edge.com/v2/public";
        _apiKey = configuration["AviationEdge:ApiKey"] ?? "";
    }

    // ========================================================
    // 1) GET /api/v1/flights/live
    // ========================================================
    public async Task<LiveFlightsResponse> GetLiveFlightsAsync(decimal? lat = null, decimal? lng = null, int? distance = null)
    {
        // Build a unique cache key based on viewport params
        var cacheKey = (lat.HasValue && lng.HasValue && distance.HasValue)
            ? $"flights:live:{lat}:{lng}:{distance}"
            : LiveFlightsCacheKey;

        // 1) Try Redis cache
        var cached = await SafeCacheGet(cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            var cachedFlights = JsonSerializer.Deserialize<List<LiveFlightDto>>(cached, JsonOptions);
            if (cachedFlights != null)
                return new LiveFlightsResponse { Count = cachedFlights.Count, Flights = cachedFlights };
        }

        // 2) Call Aviation Edge /flights
        try
        {
            var client = _httpClientFactory.CreateClient("AviationEdge");
            var url = $"{_baseUrl}/flights?key={_apiKey}&limit=300";

            // Add viewport filter params if provided
            if (lat.HasValue && lng.HasValue && distance.HasValue)
                url += $"&lat={lat}&lng={lng}&distance={distance}";

            Console.WriteLine($"[FlightTracker] 🌐 GET {url}");
            var response = await client.GetAsync(url);

            Console.WriteLine($"[FlightTracker] 📡 Status: {(int)response.StatusCode} {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[FlightTracker] ❌ Error body: {errBody[..Math.Min(errBody.Length, 500)]}");
                return new LiveFlightsResponse();
            }

            var json = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[FlightTracker] 📦 Response length: {json.Length} chars");
            Console.WriteLine($"[FlightTracker] 📦 First 300 chars: {json[..Math.Min(json.Length, 300)]}");

            // Check if response is an error object instead of array
            if (json.TrimStart().StartsWith("{"))
            {
                Console.WriteLine($"[FlightTracker] ⚠️ API returned error object: {json[..Math.Min(json.Length, 200)]}");
                return new LiveFlightsResponse();
            }

            var rawFlights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);

            if (rawFlights == null || rawFlights.Count == 0)
            {
                Console.WriteLine($"[FlightTracker] ⚠️ Deserialized to null or empty list");
                return new LiveFlightsResponse();
            }

            Console.WriteLine($"[FlightTracker] ✅ Parsed {rawFlights.Count} raw flights");

            // 3) Map — Bulk API uses "geography" + "speed" at top level (NOT flightPositions)
            //    Also uses "iataCode" for airports (not "iataNumber")
            var flights = new List<LiveFlightDto>();
            foreach (var f in rawFlights)
            {
                try
                {
                    var dto = new LiveFlightDto
                    {
                        FlightIata = GetNestedString(f, "flight", "iataNumber"),
                        Latitude = GetNestedDecimal(f, "geography", "latitude"),
                        Longitude = GetNestedDecimal(f, "geography", "longitude"),
                        Altitude = GetNestedDecimal(f, "geography", "altitude"),
                        Heading = GetNestedDecimal(f, "geography", "direction"),
                        Speed = GetNestedDecimal(f, "speed", "horizontal"),
                        IsOnGround = GetNestedInt(f, "speed", "isGround") == 1,
                        Status = GetString(f, "status"),
                        AirlineIata = GetNestedString(f, "airline", "iataCode"),
                        Registration = GetNestedString(f, "aircraft", "regNumber"),
                        DepartureIata = GetNestedString(f, "departure", "iataCode"),
                        ArrivalIata = GetNestedString(f, "arrival", "iataCode"),
                        ScheduledDeparture = GetNestedString(f, "departure", "scheduledTime"),
                        ScheduledArrival = GetNestedString(f, "arrival", "scheduledTime")
                    };

                    if (!string.IsNullOrEmpty(dto.FlightIata)
                        && dto.FlightIata != "XXD"
                        && !string.IsNullOrEmpty(dto.AirlineIata)
                        && dto.AirlineIata != "XXB"
                        && dto.Latitude != 0
                        && dto.Longitude != 0)
                    {
                        flights.Add(dto);
                    }
                }
                catch { /* Skip malformed entries */ }
            }

            // 4) Cache in Redis
            await SafeCacheSet(cacheKey, JsonSerializer.Serialize(flights), LiveFlightsTtl);

            Console.WriteLine($"[FlightTracker] ✅ Cached {flights.Count} flights");
            return new LiveFlightsResponse { Count = flights.Count, Flights = flights };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FlightTracker] ❌ Live flights error: {ex.Message}");
            return new LiveFlightsResponse();
        }
    }

    // ========================================================
    // 2) GET /api/v1/flights/search?q=FR421
    // ========================================================
    public async Task<FlightSearchResponse> SearchAsync(string q)
    {
        var result = new FlightSearchResponse();

        // 1) Aviation Edge /autocomplete for airports
        try
        {
            var client = _httpClientFactory.CreateClient("AviationEdge");
            var url = $"{_baseUrl}/autocomplete?key={_apiKey}&city={Uri.EscapeDataString(q)}";

            Console.WriteLine($"[FlightTracker] 🔍 Autocomplete: {q}");
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var autocompleteResult = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);

                if (autocompleteResult.TryGetProperty("airportsByCities", out var airports) &&
                    airports.ValueKind == JsonValueKind.Array)
                {
                    foreach (var airport in airports.EnumerateArray())
                    {
                        try
                        {
                            result.Airports.Add(new AirportSearchItem
                            {
                                IataCode = GetString(airport, "codeIataAirport"),
                                IcaoCode = GetString(airport, "codeIcaoAirport"),
                                Name = GetString(airport, "nameAirport"),
                                City = GetString(airport, "nameCountry"),
                                Latitude = GetDecimal(airport, "latitudeAirport"),
                                Longitude = GetDecimal(airport, "longitudeAirport")
                            });
                        }
                        catch { /* Skip malformed */ }
                    }
                }
            }
        }
        catch { /* Autocomplete failed */ }

        // 2) If airports found from autocomplete → search flights departing/arriving from them
        if (result.Airports.Any())
        {
            var iataCodes = result.Airports.Select(a => a.IataCode).ToList();

            var cachedForAirports = await SafeCacheGet(LiveFlightsCacheKey);
            if (!string.IsNullOrEmpty(cachedForAirports))
            {
                var liveFlights = JsonSerializer.Deserialize<List<LiveFlightDto>>(cachedForAirports, JsonOptions);
                if (liveFlights != null)
                {
                    result.Flights = liveFlights
                        .Where(f =>
                            iataCodes.Contains(f.DepartureIata, StringComparer.OrdinalIgnoreCase) ||
                            iataCodes.Contains(f.ArrivalIata, StringComparer.OrdinalIgnoreCase))
                        .Take(5)
                        .Select(f => new FlightSearchItem
                        {
                            FlightIata = f.FlightIata,
                            AirlineIata = f.AirlineIata,
                            Registration = f.Registration,
                            Status = f.Status,
                            Altitude = f.Altitude,
                            DepartureIata = f.DepartureIata,
                            ArrivalIata = f.ArrivalIata
                        })
                        .ToList();
                }
            }
        }

        // 3) Search in Redis cached live flights by flight number
        try
        {
            var cached = await SafeCacheGet(LiveFlightsCacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var liveFlights = JsonSerializer.Deserialize<List<LiveFlightDto>>(cached, JsonOptions);
                if (liveFlights != null)
                {
                    result.Flights = liveFlights
                        .Where(f => f.FlightIata.Contains(q, StringComparison.OrdinalIgnoreCase))
                        .Take(5)
                        .Select(f => new FlightSearchItem
                        {
                            FlightIata = f.FlightIata,
                            AirlineIata = f.AirlineIata,
                            Registration = f.Registration,
                            Status = f.Status,
                            Altitude = f.Altitude,
                            DepartureIata = f.DepartureIata,
                            ArrivalIata = f.ArrivalIata
                        })
                        .ToList();
                }
            }
        }
        catch { /* Cache miss */ }

        // 3) If no flights found in cache, call Aviation Edge API directly
        if (result.Flights.Count == 0)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AviationEdge");
                string url;

                // 2 letters only → could be airline code (TK, LH, FR)
                if (q.Length == 2 && q.All(char.IsLetter))
                {
                    url = $"{_baseUrl}/flights?key={_apiKey}&airlineIata={q.ToUpper()}&limit=10";
                    Console.WriteLine($"[FlightTracker] 🔍 Searching by airlineIata: {q.ToUpper()}");
                }
                else
                {
                    url = $"{_baseUrl}/flights?key={_apiKey}&flightIata={q.ToUpper()}";
                    Console.WriteLine($"[FlightTracker] 🔍 Searching by flightIata: {q.ToUpper()}");
                }

                var response = await client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    // Skip if API returned error object
                    if (!json.TrimStart().StartsWith("{"))
                    {
                        var flights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);

                        if (flights != null && flights.Count > 0)
                        {
                            Console.WriteLine($"[FlightTracker] ✅ API returned {flights.Count} flight(s) for search");

                            // Single flight API → uses flightPositions[] and departure.iataNumber
                            result.Flights = flights.Take(5).Select(f =>
                            {
                                var lastPos = GetLastPosition(f);
                                return new FlightSearchItem
                                {
                                    FlightIata = GetNestedString(f, "flight", "iataNumber"),
                                    AirlineIata = GetNestedString(f, "airline", "iataCode"),
                                    Registration = GetNestedString(f, "aircraft", "regNumber"),
                                    Status = GetString(f, "status"),
                                    Altitude = lastPos.HasValue
                                        ? GetDecimalFromElement(lastPos.Value, "altitude")
                                        : GetNestedDecimal(f, "geography", "altitude"),
                                    DepartureIata = GetNestedString(f, "departure", "iataNumber").Length > 0
                                        ? GetNestedString(f, "departure", "iataNumber")
                                        : GetNestedString(f, "departure", "iataCode"),
                                    ArrivalIata = GetNestedString(f, "arrival", "iataNumber").Length > 0
                                        ? GetNestedString(f, "arrival", "iataNumber")
                                        : GetNestedString(f, "arrival", "iataCode")
                                };
                            })
                            .Where(f => !string.IsNullOrEmpty(f.FlightIata))
                            .ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlightTracker] ❌ Flight search API error: {ex.Message}");
            }
        }

        return result;
    }

    // ========================================================
    // 3) GET /api/v1/flights/{flightIata}/details
    // ========================================================
    public async Task<FlightDetailsResponse?> GetFlightDetailsAsync(string flightIata)
    {
        // Reject unknown/invalid flight numbers
        if (string.IsNullOrEmpty(flightIata)
            || flightIata.Equals("XXD", StringComparison.OrdinalIgnoreCase))
            return null;

        // ── Step 1: Aviation Edge /flights?flightIata=XX ──
        JsonElement? rawFlight = null;
        LiveFlightDto? liveData = null;
        List<FlightTrailPoint> trail = new();

        // Try Redis cache first
        try
        {
            var cached = await SafeCacheGet(LiveFlightsCacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var liveFlights = JsonSerializer.Deserialize<List<LiveFlightDto>>(cached, JsonOptions);
                liveData = liveFlights?.FirstOrDefault(
                    f => f.FlightIata.Equals(flightIata, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch { /* Cache miss */ }

        // Always call API for full data (we need flightPositions for the trail)
        try
        {
            var client = _httpClientFactory.CreateClient("AviationEdge");
            var url = $"{_baseUrl}/flights?key={_apiKey}&flightIata={Uri.EscapeDataString(flightIata)}";

            Console.WriteLine($"[FlightTracker] ✈️ GET {url}");
            var response = await client.GetAsync(url);
            Console.WriteLine($"[FlightTracker] 📡 Status: {(int)response.StatusCode} {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[FlightTracker] 📦 Response length: {json.Length} chars");
                Console.WriteLine($"[FlightTracker] 📦 First 500 chars: {json[..Math.Min(json.Length, 500)]}");

                // API returns {"error":"..."} when flight not found (object, not array)
                if (json.TrimStart().StartsWith("{"))
                {
                    Console.WriteLine($"[FlightTracker] ⚠️ API returned error/object: {json[..Math.Min(json.Length, 200)]}");
                }
                else
                {
                    var flights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);

                    if (flights != null && flights.Count > 0)
                    {
                        Console.WriteLine($"[FlightTracker] ✅ Found {flights.Count} flight(s)");
                        rawFlight = flights[0];
                        var f = rawFlight.Value;
                        var lastPos = GetLastPosition(f);

                        if (lastPos != null)
                        {
                            // Single flight API → uses flightPositions[] for position
                            liveData = new LiveFlightDto
                            {
                                FlightIata = GetNestedString(f, "flight", "iataNumber"),
                                Latitude = GetDecimalFromElement(lastPos.Value, "latitude"),
                                Longitude = GetDecimalFromElement(lastPos.Value, "longitude"),
                                Altitude = GetDecimalFromElement(lastPos.Value, "altitude"),
                                Heading = GetDecimalFromElement(lastPos.Value, "direction"),
                                Speed = GetDecimalFromElement(lastPos.Value, "horizontal_speed"),
                                IsOnGround = GetIntFromElement(lastPos.Value, "isGround") == 1,
                                Status = GetString(f, "status"),
                                AirlineIata = GetNestedString(f, "airline", "iataCode"),
                                Registration = GetNestedString(f, "aircraft", "regNumber"),
                                DepartureIata = GetNestedString(f, "departure", "iataNumber"),
                                ArrivalIata = GetNestedString(f, "arrival", "iataNumber"),
                                ScheduledDeparture = GetNestedString(f, "departure", "scheduledTime"),
                                ScheduledArrival = GetNestedString(f, "arrival", "scheduledTime")
                            };
                        }

                        // Extract flight trail from flightPositions
                        trail = ExtractFlightTrail(f);
                    }
                }
            }
            else
            {
                var errBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[FlightTracker] ❌ Details API error: {(int)response.StatusCode} - {errBody[..Math.Min(errBody.Length, 300)]}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FlightTracker] ❌ Flight API error: {ex.Message}");
        }

        if (liveData == null)
            return null;

        // ── Step 2: Timetable for schedule details (delay, gate, actual times) ──
        TimetableData? timetable = null;
        if (!string.IsNullOrEmpty(liveData.DepartureIata))
        {
            timetable = await GetTimetableDataAsync(liveData.DepartureIata, flightIata);
        }

        // ── Step 3: Local DB enrichment (seeded tables) ──
        // Airport → city name + GMT
        var depAirport = await _db.Airports
            .Include(a => a.City)
            .FirstOrDefaultAsync(a => a.CodeIataAirport == liveData.DepartureIata);

        var arrAirport = await _db.Airports
            .Include(a => a.City)
            .FirstOrDefaultAsync(a => a.CodeIataAirport == liveData.ArrivalIata);

        // Airline → name + logo
        var airline = await _db.Airlines
            .FirstOrDefaultAsync(a => a.CodeIataAirline == liveData.AirlineIata);

        // Aircraft → model text (by registration number)
        var aircraft = !string.IsNullOrEmpty(liveData.Registration)
            ? await _db.Aircrafts.FirstOrDefaultAsync(a => a.NumberRegistration == liveData.Registration)
            : null;

        // ── Step 4: Build response ──
        var scheduledDep = ParseTimeFromScheduled(liveData.ScheduledDeparture);
        var scheduledArr = ParseTimeFromScheduled(liveData.ScheduledArrival);

        var result = new FlightDetailsResponse
        {
            FlightIata = liveData.FlightIata,
            AirlineName = airline?.NameAirline ?? timetable?.AirlineName ?? liveData.AirlineIata,
            AirlineLogoUrl = airline?.LogoUrl,
            From = liveData.DepartureIata,
            FromCity = depAirport?.City?.NameCity ?? depAirport?.NameAirport ?? "",
            To = liveData.ArrivalIata,
            ToCity = arrAirport?.City?.NameCity ?? arrAirport?.NameAirport ?? "",
            UtcFrom = FormatUtc(depAirport?.GMT),
            UtcTo = FormatUtc(arrAirport?.GMT),
            Aircraft = new AircraftInfo
            {
                Registration = liveData.Registration,
                ModelText = aircraft?.ProductionLine ?? aircraft?.PlaneModel ?? GetAircraftModelFromApi(rawFlight),
                ImageUrl = null
            },
            Speed = liveData.Speed,
            Altitude = liveData.Altitude,
            DepartureGate = timetable?.DepartureGate,
            DepartureTerminal = timetable?.DepartureTerminal,
            ArrivalGate = timetable?.ArrivalGate,
            ArrivalTerminal = timetable?.ArrivalTerminal,
            ScheduledDeparture = scheduledDep,
            ActualDeparture = timetable?.ActualDeparture ?? "",
            ScheduledArrival = scheduledArr,
            EstimatedArrival = timetable?.EstimatedArrival ?? scheduledArr,
            DelayMessage = BuildDelayMessage(timetable?.DepartureDelay),
            Status = liveData.Status,
            CurrentPosition = new FlightPosition
            {
                Latitude = liveData.Latitude,
                Longitude = liveData.Longitude,
                Heading = liveData.Heading,
                Speed = liveData.Speed,
                Altitude = liveData.Altitude,
                IsOnGround = liveData.IsOnGround
            },
            FlightTrail = trail
        };

        return result;
    }

    // ========================================================
    // Timetable Helper (cached 3 min)
    // ========================================================
    private async Task<TimetableData?> GetTimetableDataAsync(string depIata, string flightIata)
    {
        var cacheKey = $"{TimetableCachePrefix}{depIata}";

        try
        {
            // Check cache
            var cached = await SafeCacheGet(cacheKey);
            List<JsonElement>? timetableFlights = null;

            if (!string.IsNullOrEmpty(cached))
            {
                timetableFlights = JsonSerializer.Deserialize<List<JsonElement>>(cached, JsonOptions);
            }
            else
            {
                // Call Aviation Edge /timetable
                var client = _httpClientFactory.CreateClient("AviationEdge");
                var url = $"{_baseUrl}/timetable?key={_apiKey}&iataCode={Uri.EscapeDataString(depIata)}&type=departure";

                Console.WriteLine($"[FlightTracker] 📋 Timetable: {depIata}");
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                timetableFlights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);

                // Cache for 3 minutes
                if (timetableFlights != null)
                {
                    await SafeCacheSet(cacheKey, json, TimetableTtl);
                }
            }

            if (timetableFlights == null) return null;

            // Find our flight in the timetable
            var match = timetableFlights.FirstOrDefault(f =>
            {
                var iata = GetNestedString(f, "flight", "iataNumber");
                return iata.Equals(flightIata, StringComparison.OrdinalIgnoreCase);
            });

            if (match.ValueKind == JsonValueKind.Undefined)
                return null;

            return new TimetableData
            {
                AirlineName = GetNestedString(match, "airline", "name"),
                DepartureDelay = GetNestedNullableInt(match, "departure", "delay"),
                DepartureGate = GetNestedStringOrNull(match, "departure", "gate"),
                DepartureTerminal = GetNestedStringOrNull(match, "departure", "terminal"),
                ArrivalGate = GetNestedStringOrNull(match, "arrival", "gate"),
                ArrivalTerminal = GetNestedStringOrNull(match, "arrival", "terminal"),
                ActualDeparture = ParseTimeFromScheduled(GetNestedString(match, "departure", "actualTime")),
                EstimatedArrival = ParseTimeFromScheduled(GetNestedString(match, "arrival", "estimatedTime")),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FlightTracker] ❌ Timetable error: {ex.Message}");
            return null;
        }
    }

    // ========================================================
    // Internal model for timetable extracted data
    // ========================================================
    private class TimetableData
    {
        public string? AirlineName { get; set; }
        public int? DepartureDelay { get; set; }
        public string? DepartureGate { get; set; }
        public string? DepartureTerminal { get; set; }
        public string? ArrivalGate { get; set; }
        public string? ArrivalTerminal { get; set; }
        public string? ActualDeparture { get; set; }
        public string? EstimatedArrival { get; set; }
    }

    // ========================================================
    // Flight Trail Extraction
    // ========================================================
    private static List<FlightTrailPoint> ExtractFlightTrail(JsonElement flight)
    {
        var trail = new List<FlightTrailPoint>();

        if (!flight.TryGetProperty("flightPositions", out var positions) ||
            positions.ValueKind != JsonValueKind.Array)
            return trail;

        foreach (var pos in positions.EnumerateArray())
        {
            // Skip string entries like "..."
            if (pos.ValueKind != JsonValueKind.Object)
                continue;

            try
            {
                trail.Add(new FlightTrailPoint
                {
                    Latitude = GetDecimalFromElement(pos, "latitude"),
                    Longitude = GetDecimalFromElement(pos, "longitude"),
                    Altitude = GetDecimalFromElement(pos, "altitude"),
                    Speed = GetDecimalFromElement(pos, "horizontal_speed"),
                    Heading = GetDecimalFromElement(pos, "direction"),
                    IsOnGround = GetIntFromElement(pos, "isGround") == 1,
                    Timestamp = GetLongFromElement(pos, "updated")
                });
            }
            catch { /* Skip malformed */ }
        }

        return trail;
    }

    // ========================================================
    // Helper: Get last position from flightPositions array
    // ========================================================
    private static JsonElement? GetLastPosition(JsonElement flight)
    {
        if (!flight.TryGetProperty("flightPositions", out var positions) ||
            positions.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement? last = null;
        foreach (var pos in positions.EnumerateArray())
        {
            if (pos.ValueKind == JsonValueKind.Object)
                last = pos;
        }
        return last;
    }

    // ========================================================
    // Safe Redis operations (won't crash if Redis is down)
    // ========================================================
    private async Task<string?> SafeCacheGet(string key)
    {
        try { return await _redis.GetAsync(key); }
        catch { return null; }
    }

    private async Task SafeCacheSet(string key, string value, TimeSpan ttl)
    {
        try { await _redis.SetAsync(key, value, ttl); }
        catch { /* Redis down — continue without caching */ }
    }

    // ========================================================
    // Utility helpers
    // ========================================================
    private static string? BuildDelayMessage(int? departureDelay)
    {
        if (departureDelay is > 0)
            return $"{departureDelay}Min delay due to air port traffic";
        return null;
    }

    private static string FormatUtc(string? gmt)
    {
        if (string.IsNullOrWhiteSpace(gmt)) return "";
        gmt = gmt.Trim();
        return gmt.StartsWith("-") ? $"UTC{gmt}" : $"UTC+{gmt}";
    }

    private static string ParseTimeFromScheduled(string? scheduledTime)
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

    private static string? GetAircraftModelFromApi(JsonElement? rawFlight)
    {
        if (rawFlight == null) return null;
        var code = GetNestedString(rawFlight.Value, "aircraft", "icaoCode");
        return string.IsNullOrEmpty(code) ? null : code;
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

    private static decimal GetNestedDecimal(JsonElement element, string obj, string prop)
    {
        if (element.TryGetProperty(obj, out var nested) &&
            nested.TryGetProperty(prop, out var value))
        {
            if (value.TryGetDecimal(out var d)) return d;
            if (decimal.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static int GetNestedInt(JsonElement element, string obj, string prop)
    {
        if (element.TryGetProperty(obj, out var nested) &&
            nested.TryGetProperty(prop, out var value))
        {
            if (value.TryGetInt32(out var i)) return i;
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return 0;
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

    private static int? GetNestedNullableInt(JsonElement element, string obj, string prop)
    {
        if (element.TryGetProperty(obj, out var nested) &&
            nested.TryGetProperty(prop, out var value))
        {
            if (value.TryGetInt32(out var i)) return i;
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static decimal GetDecimalFromElement(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value))
        {
            if (value.TryGetDecimal(out var d)) return d;
            if (decimal.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static int GetIntFromElement(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value))
        {
            if (value.TryGetInt32(out var i)) return i;
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static long GetLongFromElement(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value))
        {
            if (value.TryGetInt64(out var l)) return l;
            if (long.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return 0;
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

    private static decimal GetDecimal(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value))
        {
            if (value.TryGetDecimal(out var d)) return d;
            if (decimal.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }
}
