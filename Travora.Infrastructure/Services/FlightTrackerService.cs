using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Travora.Application.DTOs.Flights.Tracker;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.Services;
using Travora.Infrastructure.Data;
using Travora.Domain.Entities;
using Travora.Domain.Enums;

namespace Travora.Infrastructure.Services;

public class FlightTrackerService : IFlightTrackerService
{
    private readonly IUpstashRedisService _redis;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationDbContext _db;
    private readonly IAdsbExchangeService _adsbService;
    private readonly ILogger<FlightTrackerService> _logger;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    private const string LiveFlightsCacheKey = "flights:live:all";
    private const string LiveFlightsTimestampKey = "flights:live:timestamp";
    private const string TimetableCachePrefix = "timetable:";

    // ADSB data updates every ~2s — cache for 3 minutes (180s) to reduce API consumption
    private static readonly TimeSpan AdsbCacheTtl = TimeSpan.FromMinutes(3);
    // Aviation Edge updates every 5-8 min — cache aligned to 5 min (used as fallback)
    private static readonly TimeSpan GlobalCacheTtl = TimeSpan.FromMinutes(5);
    // Timetables are semi-static — 15 min is safe
    private static readonly TimeSpan TimetableTtl = TimeSpan.FromMinutes(15);
    // Flights not seen for this long are evicted from the merge pool
    private static readonly TimeSpan StaleFlightThreshold = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FlightTrackerService(
        IUpstashRedisService redis,
        IHttpClientFactory httpClientFactory,
        ApplicationDbContext db,
        IAdsbExchangeService adsbService,
        ILogger<FlightTrackerService> logger,
        IConfiguration configuration)
    {
        _redis = redis;
        _httpClientFactory = httpClientFactory;
        _db = db;
        _adsbService = adsbService;
        _logger = logger;
        _baseUrl = configuration["AviationEdge:BaseUrl"] ?? "https://aviation-edge.com/v2/public";
        _apiKey = configuration["AviationEdge:ApiKey"] ?? "";
    }

    // ========================================================
    // 1) GET /api/v1/flights/live
    //    Primary: ADSBexchange (real-time radar, ~2s updates)
    //    Fallback: Aviation Edge (schedule-enriched, 5-8 min updates)
    // ========================================================
    public async Task<ViewportFlightsResponse> GetViewportFlightsAsync(
        decimal minLat, decimal maxLat, decimal minLng, decimal maxLng,
        decimal? centerLat = null, decimal? centerLng = null, int? distance = null)
    {
        // ----- Step 1: Compute center & radius from viewport bounds -----
        var cLat = centerLat ?? (minLat + maxLat) / 2;
        var cLon = centerLng ?? (minLng + maxLng) / 2;

        if (!distance.HasValue)
        {
            // Convert bounding box diagonal to NM (1 deg lat ≈ 60 NM)
            var latSpanNm = (double)(maxLat - minLat) * 60;
            var lonSpanNm = (double)(maxLng - minLng) * 60 * Math.Cos((double)cLat * Math.PI / 180);
            var diagonalNm = Math.Sqrt(latSpanNm * latSpanNm + lonSpanNm * lonSpanNm);
            distance = (int)Math.Clamp(diagonalNm / 2, 5, 750);
        }
        else
        {
            distance = Math.Clamp(distance.Value, 5, 750);
        }

        // ----- Step 2: Try ADSB cache first (Raw aircraft list) -----
        List<AdsbAircraftDto>? adsbResults = null;
        var adsbCacheKey = $"adsb:raw:{cLat:F1}:{cLon:F1}:{distance}";
        
        try
        {
            var cached = await SafeCacheGet(adsbCacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                adsbResults = JsonSerializer.Deserialize<List<AdsbAircraftDto>>(cached, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading raw ADSB cache");
        }

        // ----- Step 3: Fetch fresh ADSB data if cache is empty -----
        long lastApiUpdate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (adsbResults == null)
        {
            try
            {
                adsbResults = await _adsbService.GetAircraftInRadiusAsync(
                    (double)cLat, (double)cLon, distance.Value);

                if (adsbResults != null && adsbResults.Count > 0)
                {
                    await SafeCacheSet(adsbCacheKey, JsonSerializer.Serialize(adsbResults), AdsbCacheTtl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ADSBexchange primary source failed, falling back to Aviation Edge");
            }
        }

        List<ViewportFlightDto>? resultFlights = null;

        if (adsbResults != null && adsbResults.Count > 0)
        {
            resultFlights = adsbResults
                .Where(a => a.Lat >= minLat && a.Lat <= maxLat && a.Lon >= minLng && a.Lon <= maxLng)
                .Select(a => new ViewportFlightDto
                {
                    Id = !string.IsNullOrEmpty(a.Callsign) ? a.Callsign : a.Hex,
                    Lat = a.Lat,
                    Lng = a.Lon,
                    Alt = a.AltitudeFt,
                    Hdg = a.Heading,
                    Spd = a.SpeedKts,
                    Gnd = a.IsOnGround,
                    Sts = a.IsOnGround ? "landed" : "en-route",
                    Airline = ExtractAirlineFromCallsign(a.Callsign),
                    Reg = a.Registration
                })
                .ToList();
        }

        // ----- Step 4: Fallback to Aviation Edge if ADSB returned nothing -----
        if (resultFlights == null || resultFlights.Count == 0)
        {
            resultFlights = await FetchAviationEdgeViewportAsync(
                minLat, maxLat, minLng, maxLng, cLat, cLon, distance.Value);
        }

        var response = new ViewportFlightsResponse
        {
            Count = resultFlights.Count,
            LastUpdated = DateTime.UtcNow,
            LastApiUpdate = lastApiUpdate,
            Flights = resultFlights
        };

        return response;
    }

    /// <summary>
    /// Fallback: fetches viewport flights from Aviation Edge (original logic).
    /// Only called when ADSBexchange is down or returns no data.
    /// </summary>
    private async Task<List<ViewportFlightDto>> FetchAviationEdgeViewportAsync(
        decimal minLat, decimal maxLat, decimal minLng, decimal maxLng,
        decimal centerLat, decimal centerLon, int distanceNm)
    {
        var flightsDict = new Dictionary<string, CachedFlight>(StringComparer.OrdinalIgnoreCase);

        // Try loading from global cache first
        var cached = await SafeCacheGet(LiveFlightsCacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            var cachedFlights = JsonSerializer.Deserialize<List<CachedFlight>>(cached, JsonOptions);
            if (cachedFlights != null)
                foreach (var f in cachedFlights)
                    flightsDict[f.FlightIata] = f;
        }

        // Fetch fresh data if cache is empty
        if (flightsDict.Count == 0)
        {
            var distanceKm = (int)(distanceNm * 1.852);
            var url = $"{_baseUrl}/flights?key={_apiKey}&lat={centerLat}&lng={centerLon}&distance={distanceKm}&limit=200&status=en-route";
            try
            {
                var client = _httpClientFactory.CreateClient("AviationEdge");
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!json.TrimStart().StartsWith("{"))
                    {
                        var rawFlights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);
                        if (rawFlights != null)
                        {
                            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            foreach (var f in rawFlights)
                            {
                                var parsed = ParseRawFlight(f, now);
                                if (parsed != null)
                                    flightsDict[parsed.FlightIata] = parsed;
                            }

                            var cutoff = now - (long)StaleFlightThreshold.TotalSeconds;
                            var activeFlights = flightsDict.Values.Where(f => f.LastSeen >= cutoff).ToList();
                            await SafeCacheSet(LiveFlightsCacheKey, JsonSerializer.Serialize(activeFlights), GlobalCacheTtl);
                            await SafeCacheSet(LiveFlightsTimestampKey, now.ToString(), GlobalCacheTtl);
                            flightsDict = activeFlights.ToDictionary(f => f.FlightIata, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch { }
        }

        return flightsDict.Values
            .Where(f => f.Lat >= minLat && f.Lat <= maxLat && f.Lng >= minLng && f.Lng <= maxLng)
            .Select(f => new ViewportFlightDto
            {
                Id = f.FlightIata, Lat = f.Lat, Lng = f.Lng, Alt = f.Alt,
                Hdg = f.Hdg, Spd = f.Spd, Gnd = f.Gnd, Sts = f.Sts,
                Airline = f.Airline, Reg = f.Reg
            })
            .ToList();
    }

    /// <summary>
    /// Extracts the airline ICAO code from a callsign (e.g. "MSR779" → "MSR", "UAE201" → "UAE").
    /// Returns empty if the callsign doesn't follow the standard pattern.
    /// </summary>
    private static string ExtractAirlineFromCallsign(string callsign)
    {
        if (string.IsNullOrEmpty(callsign) || callsign.Length < 4) return string.Empty;

        // Standard airline callsigns: 2-3 letter prefix followed by digits
        int firstDigit = -1;
        for (int i = 0; i < callsign.Length; i++)
        {
            if (char.IsDigit(callsign[i])) { firstDigit = i; break; }
        }

        if (firstDigit >= 2 && firstDigit <= 3)
            return callsign[..firstDigit];

        return string.Empty;
    }

    // ========================================================
    // 2) GET /api/v1/flights/search?q=FR421
    // ========================================================
    public async Task<FlightSearchResponse> SearchAsync(string q)
    {
        var result = new FlightSearchResponse();

        try
        {
            var client = _httpClientFactory.CreateClient("AviationEdge");
            var url = $"{_baseUrl}/autocomplete?key={_apiKey}&city={Uri.EscapeDataString(q)}";

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
                                City = GetString(airport, "nameCity"),   // ✅ nameCity not nameCountry
                                Latitude = GetDecimal(airport, "latitudeAirport"),
                                Longitude = GetDecimal(airport, "longitudeAirport")
                            });
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        if (result.Airports.Any())
        {
            var iataCodes = result.Airports.Select(a => a.IataCode).ToList();
            try
            {
                var cached = await SafeCacheGet(LiveFlightsCacheKey);
                if (!string.IsNullOrEmpty(cached))
                {
                    var liveFlights = JsonSerializer.Deserialize<List<CachedFlight>>(cached, JsonOptions);
                    if (liveFlights != null)
                    {
                        result.Flights = liveFlights
                            .Where(f =>
                                iataCodes.Contains(f.Dep, StringComparer.OrdinalIgnoreCase) ||
                                iataCodes.Contains(f.Arr, StringComparer.OrdinalIgnoreCase))
                            .Take(5)
                            .Select(f => new FlightSearchItem
                            {
                                FlightIata = f.FlightIata,
                                AirlineIata = f.Airline,
                                Registration = f.Reg,
                                Status = f.Sts,
                                Altitude = f.Alt,
                                DepartureIata = f.Dep,
                                ArrivalIata = f.Arr
                            })
                            .ToList();
                    }
                }
            }
            catch { }
        }

        try
        {
            var cached = await SafeCacheGet(LiveFlightsCacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var liveFlights = JsonSerializer.Deserialize<List<CachedFlight>>(cached, JsonOptions);
                if (liveFlights != null)
                {
                    var matched = liveFlights
                        .Where(f => f.FlightIata.Contains(q, StringComparison.OrdinalIgnoreCase))
                        .Take(5)
                        .Select(f => new FlightSearchItem
                        {
                            FlightIata = f.FlightIata,
                            AirlineIata = f.Airline,
                            Registration = f.Reg,
                            Status = f.Sts,
                            Altitude = f.Alt,
                            DepartureIata = f.Dep,
                            ArrivalIata = f.Arr
                        });

                    foreach(var m in matched)
                    {
                        if (!result.Flights.Any(f => f.FlightIata == m.FlightIata))
                        {
                            result.Flights.Add(m);
                        }
                    }
                }
            }
        }
        catch { }

        if (result.Flights.Count == 0)
        {
            try
            {
                var adsbAircraft = await _adsbService.GetAircraftByCallsignAsync(q);
                if (adsbAircraft != null && !string.IsNullOrEmpty(adsbAircraft.Callsign))
                {
                    string airlineIata = "";
                    int firstDigitIndex = adsbAircraft.Callsign.TakeWhile(c => !char.IsDigit(c)).Count();
                    if (firstDigitIndex > 0 && firstDigitIndex <= 3)
                    {
                        var prefix = adsbAircraft.Callsign[..firstDigitIndex];
                        if (prefix.Length == 3)
                        {
                            var airline = await _db.Airlines.AsNoTracking().FirstOrDefaultAsync(a => a.CodeIcaoAirline == prefix);
                            airlineIata = airline?.CodeIataAirline ?? prefix;
                        }
                        else
                        {
                            airlineIata = prefix;
                        }
                    }

                    string depIata = "";
                    string arrIata = "";
                    string depScheduled = "";
                    string depActual = "";
                    string arrScheduled = "";
                    string arrEstimated = "";
                    string icaoModel = "";
                    try
                    {
                        var searchIata = await ConvertIcaoCallsignToIataAsync(adsbAircraft.Callsign.ToUpper());
                        var client = _httpClientFactory.CreateClient("AviationEdge");
                        string aeUrl = $"{_baseUrl}/flights?key={_apiKey}&flightIata={Uri.EscapeDataString(searchIata)}";
                        var aeResponse = await client.GetAsync(aeUrl);
                        string aeJson = "";
                        if (aeResponse.IsSuccessStatusCode)
                        {
                            aeJson = await aeResponse.Content.ReadAsStringAsync();
                        }

                        // Fallback: If query by flightIata failed or returned empty/error, try query by flightIcao
                        if ((string.IsNullOrEmpty(aeJson) || aeJson.Contains("error") || aeJson.Trim() == "[]") && !string.IsNullOrEmpty(adsbAircraft.Callsign))
                        {
                            var icaoUrl = $"{_baseUrl}/flights?key={_apiKey}&flightIcao={Uri.EscapeDataString(adsbAircraft.Callsign.ToUpper())}";
                            var icaoResponse = await client.GetAsync(icaoUrl);
                            if (icaoResponse.IsSuccessStatusCode)
                            {
                                var icaoJson = await icaoResponse.Content.ReadAsStringAsync();
                                if (!string.IsNullOrEmpty(icaoJson) && !icaoJson.Contains("error") && icaoJson.Trim() != "[]")
                                {
                                    aeJson = icaoJson;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(aeJson) && !aeJson.TrimStart().StartsWith("{"))
                        {
                            var aeFlights = JsonSerializer.Deserialize<List<JsonElement>>(aeJson, JsonOptions);
                            if (aeFlights != null && aeFlights.Count > 0)
                            {
                                var first = aeFlights[0];
                                depIata = GetNestedString(first, "departure", "iataCode");
                                arrIata = GetNestedString(first, "arrival", "iataCode");
                                depScheduled = ParseTimeFromScheduled(GetNestedString(first, "departure", "scheduledTime"));
                                depActual = ParseTimeFromScheduled(GetNestedString(first, "departure", "actualTime") ?? GetNestedString(first, "departure", "estimatedTime"));
                                arrScheduled = ParseTimeFromScheduled(GetNestedString(first, "arrival", "scheduledTime"));
                                arrEstimated = ParseTimeFromScheduled(GetNestedString(first, "arrival", "estimatedTime") ?? GetNestedString(first, "arrival", "actualTime"));
                                icaoModel = GetNestedString(first, "aircraft", "icaoCode") ?? GetNestedString(first, "aircraft", "iataCode");
                            }
                        }
                    }
                    catch { }

                    result.Flights.Add(new FlightSearchItem
                    {
                        FlightIata = adsbAircraft.Callsign,
                        AirlineIata = airlineIata,
                        Registration = adsbAircraft.Registration,
                        Status = adsbAircraft.IsOnGround ? "Landed" : "Active",
                        Altitude = adsbAircraft.AltitudeFt,
                        DepartureIata = depIata,
                        ArrivalIata = arrIata,
                        AircraftModel = icaoModel
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error doing ADSBexchange callsign search fallback: {Msg}", ex.Message);
            }
        }

        if (result.Flights.Count == 0)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AviationEdge");
                var searchIata = await ConvertIcaoCallsignToIataAsync(q.ToUpper());
                string url = q.Length == 2 && q.All(char.IsLetter)
                    ? $"{_baseUrl}/flights?key={_apiKey}&airlineIata={q.ToUpper()}&limit=10"
                    : $"{_baseUrl}/flights?key={_apiKey}&flightIata={Uri.EscapeDataString(searchIata)}";

                var response = await client.GetAsync(url);
                string json = "";
                if (response.IsSuccessStatusCode)
                {
                    json = await response.Content.ReadAsStringAsync();
                }

                // Fallback: If query by flightIata failed or returned empty/error, try query by flightIcao
                if (!(q.Length == 2 && q.All(char.IsLetter)) && 
                    (string.IsNullOrEmpty(json) || json.Contains("error") || json.Trim() == "[]") && 
                    !string.IsNullOrEmpty(q))
                {
                    var icaoUrl = $"{_baseUrl}/flights?key={_apiKey}&flightIcao={Uri.EscapeDataString(q.ToUpper())}";
                    var icaoResponse = await client.GetAsync(icaoUrl);
                    if (icaoResponse.IsSuccessStatusCode)
                    {
                        var icaoJson = await icaoResponse.Content.ReadAsStringAsync();
                        if (!string.IsNullOrEmpty(icaoJson) && !icaoJson.Contains("error") && icaoJson.Trim() != "[]")
                        {
                            json = icaoJson;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(json) && !json.TrimStart().StartsWith("{"))
                {
                    var flights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);
                    if (flights != null && flights.Count > 0)
                    {
                        result.Flights.AddRange(flights.Take(5).Select(f =>
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
                                DepartureIata = GetNestedString(f, "departure", "iataCode"),
                                ArrivalIata = GetNestedString(f, "arrival", "iataCode"),
                                AircraftModel = GetNestedString(f, "aircraft", "icaoCode") ?? GetNestedString(f, "aircraft", "iataCode")
                            };
                        })
                        .Where(f => !string.IsNullOrEmpty(f.FlightIata)));
                    }
                }
            }
            catch { }
        }

        await EnrichSearchFlightsAsync(result.Flights);

        return result;
    }

    // ========================================================
    // 3) GET /api/v1/flights/{flightIata}/details
    // ========================================================
    public async Task<FlightDetailsResponse?> GetFlightDetailsAsync(string flightIata)
    {
        if (string.IsNullOrEmpty(flightIata) || flightIata.Equals("XXD", StringComparison.OrdinalIgnoreCase))
            return null;

        JsonElement? rawFlight = null;
        CachedFlight? liveData = null;
        List<FlightTrailPoint> trail = new();

        // 1. Try to get real-time info from ADSBexchange first, as it is our primary live provider
        AdsbAircraftDto? adsbLive = null;
        try
        {
            // If the key is exactly 6 chars and is a valid hexadecimal string, treat it as ICAO Hex ID
            if (flightIata.Length == 6 && flightIata.All(c => "0123456789ABCDEFabcdef".Contains(c)))
            {
                adsbLive = await _adsbService.GetAircraftByIcaoAsync(flightIata);
            }
            else
            {
                var targetCallsign = await ConvertIataCallsignToIcaoAsync(flightIata);
                adsbLive = await _adsbService.GetAircraftByCallsignAsync(targetCallsign);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching live details from ADSBexchange for {Id}", flightIata);
        }

        if (adsbLive != null)
        {
            liveData = new CachedFlight
            {
                FlightIata = !string.IsNullOrEmpty(adsbLive.Callsign) ? adsbLive.Callsign : adsbLive.Hex,
                Lat = adsbLive.Lat,
                Lng = adsbLive.Lon,
                Alt = adsbLive.AltitudeFt,
                Hdg = adsbLive.Heading,
                Spd = adsbLive.SpeedKts,
                Gnd = adsbLive.IsOnGround,
                Sts = adsbLive.IsOnGround ? "landed" : "en-route",
                Airline = ExtractAirlineFromCallsign(adsbLive.Callsign),
                Reg = adsbLive.Registration,
                AircraftType = adsbLive.AircraftType
            };
        }

        // 2. If ADSB failed, fallback to global live cache (Aviation Edge)
        if (liveData == null)
        {
            try
            {
                var cached = await SafeCacheGet(LiveFlightsCacheKey);
                if (!string.IsNullOrEmpty(cached))
                {
                    var cachedFlights = JsonSerializer.Deserialize<List<CachedFlight>>(cached, JsonOptions);
                    liveData = cachedFlights?.FirstOrDefault(f => f.FlightIata.Equals(flightIata, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { }
        }

        // Determine what callsign/IATA to use for Aviation Edge query
        string targetFlight = liveData?.FlightIata ?? flightIata;
        string regNumber = liveData?.Reg ?? string.Empty;

        // Convert the targetFlight to IATA flight number if it's an ICAO callsign (e.g. MSR779 -> MS779)
        string iataFlight = await ConvertIcaoCallsignToIataAsync(targetFlight);

        // 3. Query Aviation Edge to enrich flight details (schedules, departure/arrival airports, breadcrumb trail)
        try
        {
            var client = _httpClientFactory.CreateClient("AviationEdge");
            string url = $"{_baseUrl}/flights?key={_apiKey}&flightIata={Uri.EscapeDataString(iataFlight)}";
            
            // If we only have registration number from ADSB (e.g. general aviation or military flights)
            if (string.IsNullOrEmpty(iataFlight) && !string.IsNullOrEmpty(regNumber))
            {
                url = $"{_baseUrl}/flights?key={_apiKey}&regNumber={Uri.EscapeDataString(regNumber)}";
            }

            var response = await client.GetAsync(url);
            string json = "";
            if (response.IsSuccessStatusCode)
            {
                json = await response.Content.ReadAsStringAsync();
            }

            // Fallback: If query by flightIata failed or returned empty/error (common for ATC callsigns like THY4VL), try query by flightIcao
            if ((string.IsNullOrEmpty(json) || json.Contains("error") || json.Trim() == "[]") && !string.IsNullOrEmpty(targetFlight))
            {
                var icaoUrl = $"{_baseUrl}/flights?key={_apiKey}&flightIcao={Uri.EscapeDataString(targetFlight)}";
                var icaoResponse = await client.GetAsync(icaoUrl);
                if (icaoResponse.IsSuccessStatusCode)
                {
                    var icaoJson = await icaoResponse.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(icaoJson) && !icaoJson.Contains("error") && icaoJson.Trim() != "[]")
                    {
                        json = icaoJson;
                    }
                }
            }

            if (!string.IsNullOrEmpty(json) && !json.TrimStart().StartsWith("{"))
            {
                var flights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);
                if (flights != null && flights.Count > 0)
                {
                    rawFlight = flights[0];
                    var f = rawFlight.Value;
                    var lastPos = GetLastPosition(f);

                    if (liveData == null)
                    {
                        liveData = ParseRawFlight(f, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        if (liveData == null && lastPos != null)
                        {
                            liveData = new CachedFlight
                            {
                                FlightIata = GetNestedString(f, "flight", "iataNumber"),
                                Lat = GetDecimalFromElement(lastPos.Value, "latitude"),
                                Lng = GetDecimalFromElement(lastPos.Value, "longitude"),
                                Alt = GetDecimalFromElement(lastPos.Value, "altitude"),
                                Hdg = GetDecimalFromElement(lastPos.Value, "direction"),
                                Spd = GetDecimalFromElement(lastPos.Value, "horizontal_speed"),
                                Gnd = GetIntFromElement(lastPos.Value, "isGround") == 1,
                                Sts = GetString(f, "status"),
                                Airline = GetNestedString(f, "airline", "iataCode"),
                                Reg = GetNestedString(f, "aircraft", "regNumber"),
                                Dep = GetNestedString(f, "departure", "iataCode"),
                                Arr = GetNestedString(f, "arrival", "iataCode"),
                                AircraftType = GetNestedString(f, "aircraft", "icaoCode") ?? GetNestedString(f, "aircraft", "iataCode")
                            };
                        }
                    }
                    else if (liveData != null)
                    {
                        // Enrich existing ADSB liveData with departure/arrival/airline from Aviation Edge
                        if (string.IsNullOrEmpty(liveData.Dep))
                            liveData.Dep = GetNestedString(f, "departure", "iataCode");
                        if (string.IsNullOrEmpty(liveData.Arr))
                            liveData.Arr = GetNestedString(f, "arrival", "iataCode");
                        if (string.IsNullOrEmpty(liveData.Airline))
                            liveData.Airline = GetNestedString(f, "airline", "iataCode");

                        var realIata = GetNestedString(f, "flight", "iataNumber");
                        if (!string.IsNullOrEmpty(realIata) && !realIata.Equals(liveData.FlightIata, StringComparison.OrdinalIgnoreCase))
                        {
                            liveData.FlightIata = realIata;
                            iataFlight = realIata;
                        }
                    }

                    trail = ExtractFlightTrail(f);
                }
            }
        }
        catch { }

        // If we still have no liveData, we cannot return flight details
        if (liveData == null) return null;

        // Fallback to Timetable by flight number if departure or arrival airports are missing
        if (string.IsNullOrEmpty(liveData.Dep) || string.IsNullOrEmpty(liveData.Arr))
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AviationEdge");
                string ttUrl = $"{_baseUrl}/timetable?key={_apiKey}&flightIata={Uri.EscapeDataString(iataFlight)}";
                var ttResponse = await client.GetAsync(ttUrl);
                if (ttResponse.IsSuccessStatusCode)
                {
                    var ttJson = await ttResponse.Content.ReadAsStringAsync();
                    if (!ttJson.TrimStart().StartsWith("{"))
                    {
                        var ttList = JsonSerializer.Deserialize<List<JsonElement>>(ttJson, JsonOptions);
                        if (ttList != null && ttList.Count > 0)
                        {
                            var entry = ttList[0];
                            if (string.IsNullOrEmpty(liveData.Dep))
                                liveData.Dep = GetNestedString(entry, "departure", "iataCode");
                            if (string.IsNullOrEmpty(liveData.Arr))
                                liveData.Arr = GetNestedString(entry, "arrival", "iataCode");
                            if (string.IsNullOrEmpty(liveData.Airline))
                                liveData.Airline = GetNestedString(entry, "airline", "iataCode");
                        }
                    }
                }
            }
            catch { }
        }

        TimetableData? timetable = null;
        if (!string.IsNullOrEmpty(liveData.Dep))
        {
            timetable = await GetTimetableDataAsync(liveData.Dep, targetFlight);
        }

        // If active telemetry has no trail history, query `/flight_track_history` to fetch the path and fallback timetable
        if (trail.Count == 0)
        {
            var codeToQuery = !string.IsNullOrEmpty(iataFlight) ? iataFlight : targetFlight;
            if (!string.IsNullOrEmpty(codeToQuery) || !string.IsNullOrEmpty(liveData.Reg))
            {
                var (historyTrail, historyTimetable) = await GetFlightTrailHistoryAsync(codeToQuery, liveData.Reg, liveData.Dep, liveData.Arr);
                trail = historyTrail;

                if (timetable == null && historyTimetable != null)
                {
                    timetable = historyTimetable;
                }
                else if (timetable != null && historyTimetable != null)
                {
                    // Merge fields
                    if (string.IsNullOrEmpty(timetable.ScheduledDeparture)) timetable.ScheduledDeparture = historyTimetable.ScheduledDeparture;
                    if (string.IsNullOrEmpty(timetable.ScheduledArrival)) timetable.ScheduledArrival = historyTimetable.ScheduledArrival;
                    if (string.IsNullOrEmpty(timetable.DepartureGate)) timetable.DepartureGate = historyTimetable.DepartureGate;
                    if (string.IsNullOrEmpty(timetable.DepartureTerminal)) timetable.DepartureTerminal = historyTimetable.DepartureTerminal;
                    if (string.IsNullOrEmpty(timetable.ArrivalGate)) timetable.ArrivalGate = historyTimetable.ArrivalGate;
                    if (string.IsNullOrEmpty(timetable.ArrivalTerminal)) timetable.ArrivalTerminal = historyTimetable.ArrivalTerminal;
                    if (string.IsNullOrEmpty(timetable.ActualDeparture)) timetable.ActualDeparture = historyTimetable.ActualDeparture;
                    if (string.IsNullOrEmpty(timetable.EstimatedArrival)) timetable.EstimatedArrival = historyTimetable.EstimatedArrival;
                    if (timetable.DepartureDelay == null) timetable.DepartureDelay = historyTimetable.DepartureDelay;
                }
            }
        }

        var depAirport = await _db.Airports.Include(a => a.City).Include(a => a.Country).FirstOrDefaultAsync(a => a.CodeIataAirport == liveData.Dep);
        var arrAirport = await _db.Airports.Include(a => a.City).Include(a => a.Country).FirstOrDefaultAsync(a => a.CodeIataAirport == liveData.Arr);
        var airline = await _db.Airlines.FirstOrDefaultAsync(a => a.CodeIataAirline == liveData.Airline || a.CodeIcaoAirline == liveData.Airline);
        var aircraft = !string.IsNullOrEmpty(liveData.Reg)
            ? await _db.Aircrafts.FirstOrDefaultAsync(a => a.NumberRegistration == liveData.Reg)
            : null;

        var rawScheduledDep = rawFlight.HasValue ? GetNestedString(rawFlight.Value, "departure", "scheduledTime") : null;
        var rawScheduledArr = rawFlight.HasValue ? GetNestedString(rawFlight.Value, "arrival", "scheduledTime") : null;

        var scheduledDep = ParseTimeFromScheduled(rawScheduledDep);
        if (string.IsNullOrEmpty(scheduledDep) && !string.IsNullOrEmpty(timetable?.ScheduledDeparture))
        {
            scheduledDep = timetable.ScheduledDeparture;
        }

        var scheduledArr = ParseTimeFromScheduled(rawScheduledArr);
        if (string.IsNullOrEmpty(scheduledArr) && !string.IsNullOrEmpty(timetable?.ScheduledArrival))
        {
            scheduledArr = timetable.ScheduledArrival;
        }

        string? hexId = adsbLive?.Hex;
        string? imageUrl = await FetchAircraftImageUrlAsync(liveData.Reg, hexId);

        var result = new FlightDetailsResponse
        {
            FlightIata = liveData.FlightIata,
            Status = liveData.Sts,
            DelayMessage = BuildDelayMessage(timetable?.DepartureDelay),
            Airline = new FlightDetailAirlineDto
            {
                Name = airline?.NameAirline ?? timetable?.AirlineName ?? liveData.Airline,
                Iata = airline?.CodeIataAirline ?? liveData.Airline,
                Callsign = airline?.Callsign,
                Logo = !string.IsNullOrEmpty(airline?.LogoUrl)
                    ? airline.LogoUrl
                    : (!string.IsNullOrEmpty(airline?.CodeIataAirline ?? liveData.Airline)
                        ? $"https://pics.avs.io/200/200/{(airline?.CodeIataAirline ?? liveData.Airline).ToUpper()}@2x.png"
                        : null)
            },
            Aircraft = new AircraftInfo
            {
                Registration = liveData.Reg,
                Model = aircraft?.ProductionLine ?? aircraft?.PlaneModel ?? MapIcaoToModelName(liveData.AircraftType) ?? GetAircraftModelFromApi(rawFlight),
                Type = aircraft?.PlaneClass ?? aircraft?.AirplaneIataType ?? liveData.AircraftType,
                ImageUrl = imageUrl
            },
            Departure = new FlightDetailAirportDto
            {
                Iata = liveData.Dep,
                Name = depAirport?.NameAirport ?? "",
                City = depAirport?.City?.NameCity ?? "",
                Country = depAirport?.Country?.CountryName ?? "",
                Utc = FormatUtc(depAirport?.GMT),
                ScheduledTime = scheduledDep ?? ""
            },
            Arrival = new FlightDetailAirportDto
            {
                Iata = liveData.Arr,
                Name = arrAirport?.NameAirport ?? "",
                City = arrAirport?.City?.NameCity ?? "",
                Country = arrAirport?.Country?.CountryName ?? "",
                Utc = FormatUtc(arrAirport?.GMT),
                ScheduledTime = scheduledArr ?? ""
            },
            Position = new FlightPosition
            {
                Latitude = liveData.Lat,
                Longitude = liveData.Lng,
                Heading = liveData.Hdg,
                Speed = liveData.Spd,
                Altitude = liveData.Alt,
                IsOnGround = liveData.Gnd
            },
            Trail = trail
        };

        return result;
    }

    /// <summary>
    /// Translates a 3-letter ICAO flight callsign (e.g., "MSR779", "UAE201") into its 2-letter IATA format ("MS779", "EK201") 
    /// by performing a fast lookup against our local Airlines database.
    /// </summary>
    private async Task<string> ConvertIcaoCallsignToIataAsync(string callsign)
    {
        if (string.IsNullOrEmpty(callsign) || callsign.Length < 4) return callsign;

        // Find the index of the first digit
        int firstDigitIndex = -1;
        for (int i = 0; i < callsign.Length; i++)
        {
            if (char.IsDigit(callsign[i]))
            {
                firstDigitIndex = i;
                break;
            }
        }

        if (firstDigitIndex < 2) return callsign;

        var icaoCode = callsign[..firstDigitIndex];
        var flightNum = callsign[firstDigitIndex..];

        // If it's already 2-letter, it's likely IATA
        if (icaoCode.Length == 2) return callsign;

        // Look up ICAO code in our DB
        var airline = await _db.Airlines
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.CodeIcaoAirline == icaoCode);

        if (airline != null && !string.IsNullOrEmpty(airline.CodeIataAirline))
        {
            return $"{airline.CodeIataAirline}{flightNum}";
        }

        return callsign;
    }

    public async Task<AirportViewportResponse> GetAirportsInViewportAsync(
        decimal minLat, decimal maxLat, decimal minLng, decimal maxLng)
    {
        var airports = await _db.Airports
            .Include(a => a.City)
            .Include(a => a.Country)
            .Where(a => a.LatitudeAirport >= minLat && a.LatitudeAirport <= maxLat &&
                        a.LongitudeAirport >= minLng && a.LongitudeAirport <= maxLng)
            .Take(100)
            .Select(a => new AirportViewportDto
            {
                Iata = a.CodeIataAirport,
                Icao = a.CodeIcaoAirport,
                Name = a.NameAirport,
                City = a.City != null ? a.City.NameCity : "",
                Country = a.Country != null ? a.Country.CountryName : "",
                Lat = a.LatitudeAirport,
                Lng = a.LongitudeAirport
            })
            .ToListAsync();

        return new AirportViewportResponse
        {
            Count = airports.Count,
            Airports = airports
        };
    }



    private async Task<TimetableData?> GetTimetableDataAsync(string depIata, string flightIata)
    {
        var cacheKey = $"{TimetableCachePrefix}{depIata}:departure";

        try
        {
            var cached = await SafeCacheGet(cacheKey);
            List<JsonElement>? timetableFlights = null;

            if (!string.IsNullOrEmpty(cached))
            {
                timetableFlights = JsonSerializer.Deserialize<List<JsonElement>>(cached, JsonOptions);
            }
            else
            {
                var client = _httpClientFactory.CreateClient("AviationEdge");
                var url = $"{_baseUrl}/timetable?key={_apiKey}&iataCode={Uri.EscapeDataString(depIata)}&type=departure";

                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!json.TrimStart().StartsWith("{"))
                    {
                        timetableFlights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);
                        if (timetableFlights != null)
                        {
                            await SafeCacheSet(cacheKey, json, TimetableTtl);
                        }
                    }
                }
            }

            if (timetableFlights == null) return null;

            var targetNum = ExtractFlightNumber(flightIata);
            var targetDesignator = ExtractAirlineDesignator(flightIata);

            // Fetch the airline from database to get both IATA and ICAO codes for robust lookup matching
            var airlineInfo = await _db.Airlines
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.CodeIataAirline == targetDesignator || a.CodeIcaoAirline == targetDesignator);

            var targetIata = airlineInfo?.CodeIataAirline ?? targetDesignator;
            var targetIco = airlineInfo?.CodeIcaoAirline ?? targetDesignator;

            var match = timetableFlights.FirstOrDefault(f =>
            {
                var iata = GetNestedString(f, "flight", "iataNumber");
                var icao = GetNestedString(f, "flight", "icaoNumber");

                var iataNum = ExtractFlightNumber(iata);
                var icaoNum = ExtractFlightNumber(icao);

                if (targetNum != null && (targetNum == iataNum || targetNum == icaoNum))
                {
                    var iataDes = ExtractAirlineDesignator(iata);
                    var icaoDes = ExtractAirlineDesignator(icao);

                    if (targetIata.Equals(iataDes, StringComparison.OrdinalIgnoreCase) ||
                        targetIata.Equals(icaoDes, StringComparison.OrdinalIgnoreCase) ||
                        targetIco.Equals(iataDes, StringComparison.OrdinalIgnoreCase) ||
                        targetIco.Equals(icaoDes, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return iata.Equals(flightIata, StringComparison.OrdinalIgnoreCase) ||
                       icao.Equals(flightIata, StringComparison.OrdinalIgnoreCase);
            });

            if (match.ValueKind == JsonValueKind.Undefined) return null;

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
                ScheduledDeparture = ParseTimeFromScheduled(GetNestedString(match, "departure", "scheduledTime")),
                ScheduledArrival = ParseTimeFromScheduled(GetNestedString(match, "arrival", "scheduledTime")),
            };
        }
        catch
        {
            return null;
        }
    }

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
        public string? ScheduledDeparture { get; set; }
        public string? ScheduledArrival { get; set; }
    }

    /// <summary>
    /// Parses a raw Aviation Edge flight JSON element into a CachedFlight.
    /// Returns null if the flight data is invalid or should be filtered out.
    /// </summary>
    private static CachedFlight? ParseRawFlight(JsonElement f, long nowUnix)
    {
        try
        {
            var flightIata = GetNestedString(f, "flight", "iataNumber");
            var airlineIata = GetNestedString(f, "airline", "iataCode");
            var lat = GetNestedDecimal(f, "geography", "latitude");
            var lng = GetNestedDecimal(f, "geography", "longitude");

            // Filter out test/invalid flights
            if (string.IsNullOrEmpty(flightIata) || flightIata == "XXD" ||
                string.IsNullOrEmpty(airlineIata) || airlineIata == "XXB" ||
                lat == 0 || lng == 0)
                return null;

            return new CachedFlight
            {
                FlightIata = flightIata,
                Lat = lat,
                Lng = lng,
                Alt = GetNestedDecimal(f, "geography", "altitude"),
                Hdg = GetNestedDecimal(f, "geography", "direction"),
                Spd = GetNestedDecimal(f, "speed", "horizontal"),
                Gnd = GetNestedInt(f, "speed", "isGround") == 1,
                Sts = GetString(f, "status"),
                Airline = airlineIata,
                Reg = GetNestedString(f, "aircraft", "regNumber"),
                Dep = GetNestedString(f, "departure", "iataCode"),
                Arr = GetNestedString(f, "arrival", "iataCode"),
                AircraftType = GetNestedString(f, "aircraft", "icaoCode") ?? GetNestedString(f, "aircraft", "iataCode"),
                LastSeen = nowUnix
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<FlightTrailPoint> ExtractFlightTrail(JsonElement flight)
    {
        var trail = new List<FlightTrailPoint>();

        if (!flight.TryGetProperty("flightPositions", out var positions) ||
            positions.ValueKind != JsonValueKind.Array)
            return trail;

        foreach (var pos in positions.EnumerateArray())
        {
            if (pos.ValueKind != JsonValueKind.Object) continue;

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
            catch { }
        }

        return trail;
    }

    private async Task<(List<FlightTrailPoint> Trail, TimetableData? Timetable)> GetFlightTrailHistoryAsync(
        string flightIata, string? registration, string? depIata, string? arrIata)
    {
        var trail = new List<FlightTrailPoint>();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");

        var result = await TryFetchHistoryAsync(flightIata, registration, depIata, arrIata, today, trail);
        if (!result.Success || trail.Count == 0)
        {
            result = await TryFetchHistoryAsync(flightIata, registration, depIata, arrIata, yesterday, trail);
        }

        return (trail, result.Timetable);
    }

    private async Task<(bool Success, TimetableData? Timetable)> TryFetchHistoryAsync(
        string flightIata, string? registration, string? depIata, string? arrIata, string dateStr,
        List<FlightTrailPoint> trail)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("AviationEdge");
            
            // Build query parameters
            var queryParams = new List<string>
            {
                $"key={_apiKey}",
                $"depDate={dateStr}"
            };

            // Either flightIata or regNum
            if (!string.IsNullOrEmpty(flightIata))
            {
                queryParams.Add($"flightIata={Uri.EscapeDataString(flightIata)}");
            }
            else if (!string.IsNullOrEmpty(registration))
            {
                queryParams.Add($"regNum={Uri.EscapeDataString(registration)}");
            }
            else
            {
                return (false, null);
            }

            // Either depIata or arrIata is required by the API!
            if (!string.IsNullOrEmpty(depIata))
            {
                queryParams.Add($"depIata={Uri.EscapeDataString(depIata)}");
            }
            else if (!string.IsNullOrEmpty(arrIata))
            {
                queryParams.Add($"arrIata={Uri.EscapeDataString(arrIata)}");
            }
            else
            {
                return (false, null);
            }

            var url = $"{_baseUrl}/flight_track_history?{string.Join("&", queryParams)}";
            var response = await client.GetAsync(url);

            // Fallback: if it fails and we have registration, try by registration + depIata
            if (!response.IsSuccessStatusCode && !string.IsNullOrEmpty(registration) && !string.IsNullOrEmpty(flightIata))
            {
                var fallbackParams = new List<string>
                {
                    $"key={_apiKey}",
                    $"depDate={dateStr}",
                    $"regNum={Uri.EscapeDataString(registration)}"
                };
                if (!string.IsNullOrEmpty(depIata)) fallbackParams.Add($"depIata={Uri.EscapeDataString(depIata)}");
                else if (!string.IsNullOrEmpty(arrIata)) fallbackParams.Add($"arrIata={Uri.EscapeDataString(arrIata)}");

                url = $"{_baseUrl}/flight_track_history?{string.Join("&", fallbackParams)}";
                response = await client.GetAsync(url);
            }

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (!json.TrimStart().StartsWith("{"))
                {
                    var historyList = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);
                    if (historyList != null && historyList.Count > 0)
                    {
                        var matchedFlight = historyList.FirstOrDefault(f =>
                            GetNestedString(f, "flight", "iataNumber").Equals(flightIata, StringComparison.OrdinalIgnoreCase) ||
                            GetNestedString(f, "flight", "icaoNumber").Equals(flightIata, StringComparison.OrdinalIgnoreCase) ||
                            (registration != null && GetNestedString(f, "aircraft", "regNumber").Equals(registration, StringComparison.OrdinalIgnoreCase))
                        );

                        var flightToUse = matchedFlight.ValueKind != JsonValueKind.Undefined ? matchedFlight : historyList[0];
                        var parsedTrail = ExtractFlightTrail(flightToUse);
                        if (parsedTrail != null && parsedTrail.Count > 0)
                        {
                            trail.Clear();
                            trail.AddRange(parsedTrail);
                            
                            var timetable = new TimetableData
                            {
                                AirlineName = GetNestedString(flightToUse, "airline", "name"),
                                DepartureDelay = GetNestedNullableInt(flightToUse, "departure", "delay"),
                                DepartureGate = GetNestedStringOrNull(flightToUse, "departure", "gate"),
                                DepartureTerminal = GetNestedStringOrNull(flightToUse, "departure", "terminal"),
                                ArrivalGate = GetNestedStringOrNull(flightToUse, "arrival", "gate"),
                                ArrivalTerminal = GetNestedStringOrNull(flightToUse, "arrival", "terminal"),
                                ActualDeparture = ParseTimeFromScheduled(GetNestedString(flightToUse, "departure", "actualTime")),
                                EstimatedArrival = ParseTimeFromScheduled(GetNestedString(flightToUse, "arrival", "estimatedTime")),
                                ScheduledDeparture = ParseTimeFromScheduled(GetNestedString(flightToUse, "departure", "scheduledTime")),
                                ScheduledArrival = ParseTimeFromScheduled(GetNestedString(flightToUse, "arrival", "scheduledTime")),
                            };
                            
                            return (true, timetable);
                        }
                    }
                }
            }
        }
        catch { }
        return (false, null);
    }

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

    private async Task<string?> SafeCacheGet(string key)
    {
        try { return await _redis.GetAsync(key); }
        catch { return null; }
    }

    private async Task SafeCacheSet(string key, string value, TimeSpan ttl)
    {
        try { await _redis.SetAsync(key, value, ttl); }
        catch { }
    }

    private static string? BuildDelayMessage(int? departureDelay)
    {
        if (departureDelay is > 0)
            return $"{departureDelay} min delay";
        return "No Delay";
    }

    private static string FormatUtc(string? gmt)
    {
        if (string.IsNullOrWhiteSpace(gmt)) return "";
        gmt = gmt.Trim();
        return gmt.StartsWith("-") ? $"UTC{gmt}" : $"UTC+{gmt}";
    }

    private static string? ParseTimeFromScheduled(string? scheduledTime)
    {
        if (string.IsNullOrWhiteSpace(scheduledTime)) return null;
        return scheduledTime.Trim();
    }

    private static string? GetAircraftModelFromApi(JsonElement? rawFlight)
    {
        if (rawFlight == null) return null;
        var code = GetNestedString(rawFlight.Value, "aircraft", "icaoCode");
        return string.IsNullOrEmpty(code) ? null : code;
    }

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
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d)) return d;
            if (decimal.TryParse(value.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return 0;
    }

    private static int GetNestedInt(JsonElement element, string obj, string prop)
    {
        if (element.TryGetProperty(obj, out var nested) &&
            nested.TryGetProperty(prop, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)) return i;
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
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)) return i;
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static decimal GetDecimalFromElement(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d)) return d;
            if (decimal.TryParse(value.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return 0;
    }

    private static int GetIntFromElement(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)) return i;
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static long GetLongFromElement(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var l)) return l;
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
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d)) return d;
            if (decimal.TryParse(value.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return 0;
    }

    private async Task<string?> FetchAircraftImageUrlAsync(string? registration, string? hex)
    {
        var key = !string.IsNullOrEmpty(registration) 
            ? registration.Trim().ToUpper() 
            : (!string.IsNullOrEmpty(hex) ? hex.Trim().ToLower() : null);

        if (string.IsNullOrEmpty(key)) return null;

        var cacheKey = $"aircraft:photo:{key}";
        
        try
        {
            var cached = await SafeCacheGet(cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                return cached == "NULL" ? null : cached;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading planespotters cache for {Key}", key);
        }

        string? imageUrl = null;
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TravoraFlightTracker/1.0 (+https://travora.com)");

            string url = !string.IsNullOrEmpty(registration)
                ? $"https://api.planespotters.net/pub/photos/reg/{Uri.EscapeDataString(registration)}"
                : $"https://api.planespotters.net/pub/photos/hex/{Uri.EscapeDataString(hex!)}";

            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("photos", out var photosProp) && 
                    photosProp.ValueKind == JsonValueKind.Array && 
                    photosProp.GetArrayLength() > 0)
                {
                    var firstPhoto = photosProp[0];
                    if (firstPhoto.TryGetProperty("thumbnail_large", out var thumbLargeProp) &&
                        thumbLargeProp.TryGetProperty("src", out var srcProp))
                    {
                        imageUrl = srcProp.GetString();
                    }
                    else if (firstPhoto.TryGetProperty("thumbnail", out var thumbProp) &&
                             thumbProp.TryGetProperty("src", out var srcProp2))
                    {
                        imageUrl = srcProp2.GetString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching photo from Planespotters API for {Key}", key);
        }

        try
        {
            await SafeCacheSet(cacheKey, imageUrl ?? "NULL", TimeSpan.FromDays(30));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error writing planespotters cache for {Key}", key);
        }

        return imageUrl;
    }

    private async Task EnrichSearchFlightsAsync(List<FlightSearchItem> flights)
    {
        if (flights == null || flights.Count == 0) return;

        var registrations = flights.Select(f => f.Registration).Where(r => !string.IsNullOrEmpty(r)).Distinct().ToList();
        var aircraftsDict = await _db.Aircrafts
            .AsNoTracking()
            .Where(a => registrations.Contains(a.NumberRegistration))
            .ToDictionaryAsync(a => a.NumberRegistration, StringComparer.OrdinalIgnoreCase);

        var airlineIatas = flights.Select(f => f.AirlineIata).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
        var airlinesDict = new Dictionary<string, Travora.Domain.Entities.Airline>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var airlinesList = await _db.Airlines.AsNoTracking().Where(a => airlineIatas.Contains(a.CodeIataAirline)).ToListAsync();
            foreach (var airline in airlinesList)
            {
                if (!string.IsNullOrEmpty(airline.CodeIataAirline))
                {
                    airlinesDict.TryAdd(airline.CodeIataAirline, airline);
                }
            }
        }
        catch { }

        // Fetch airports & their cities/GMT details
        var airportIatas = flights.Select(f => f.DepartureIata).Concat(flights.Select(f => f.ArrivalIata))
            .Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
        var airportsDict = new Dictionary<string, Travora.Domain.Entities.Airport>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var airportsList = await _db.Airports.Include(a => a.City).AsNoTracking().Where(a => airportIatas.Contains(a.CodeIataAirport)).ToListAsync();
            foreach (var airport in airportsList)
            {
                if (!string.IsNullOrEmpty(airport.CodeIataAirport))
                {
                    airportsDict.TryAdd(airport.CodeIataAirport, airport);
                }
            }
        }
        catch { }

        var imageTasks = flights.Select(async f =>
        {
            if (!string.IsNullOrEmpty(f.Registration))
            {
                f.AircraftImageUrl = await FetchAircraftImageUrlAsync(f.Registration, null);
            }
        }).ToList();

        await Task.WhenAll(imageTasks);

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

            if (aircraftsDict.TryGetValue(f.Registration, out var matchedAircraft))
            {
                f.AircraftModel = matchedAircraft.ProductionLine ?? matchedAircraft.PlaneModel;
            }

            // Fallback: If AircraftModel is still null/empty or is an ICAO code, Map to human readable
            if (!string.IsNullOrEmpty(f.AircraftModel))
            {
                f.AircraftModel = MapIcaoToModelName(f.AircraftModel);
            }

            f.AircraftCountry = GetCountryFromRegistration(f.Registration);

            // Populate departure airport details
            if (!string.IsNullOrEmpty(f.DepartureIata) && airportsDict.TryGetValue(f.DepartureIata, out var depAirport))
            {
                f.DepartureAirportName = depAirport.NameAirport;
                f.DepartureCity = depAirport.City?.NameCity ?? "";
                f.DepartureUtc = FormatUtc(depAirport.GMT);
            }

            // Populate arrival airport details
            if (!string.IsNullOrEmpty(f.ArrivalIata) && airportsDict.TryGetValue(f.ArrivalIata, out var arrAirport))
            {
                f.ArrivalAirportName = arrAirport.NameAirport;
                f.ArrivalCity = arrAirport.City?.NameCity ?? "";
                f.ArrivalUtc = FormatUtc(arrAirport.GMT);
            }

            // Fetch delay details from Timetable API
            if (!string.IsNullOrEmpty(f.DepartureIata))
            {
                try
                {
                    var timetable = await GetTimetableDataAsync(f.DepartureIata, f.FlightIata);
                    if (timetable != null)
                    {
                        f.Delay = timetable.DepartureDelay is > 0
                            ? $"{timetable.DepartureDelay} min delay"
                            : "No delay";
                    }
                    else
                    {
                        f.Delay = "No delay";
                    }
                }
                catch 
                {
                    f.Delay = "No delay";
                }
            }
            else
            {
                f.Delay = "No delay";
            }
        }
    }

    private static string MapIcaoToModelName(string code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        code = code.Trim().ToUpperInvariant();
        
        return code switch
        {
            "B738" => "Boeing 737-800",
            "B739" => "Boeing 737-900",
            "B737" => "Boeing 737",
            "A320" => "Airbus A320",
            "A321" => "Airbus A321",
            "A21N" => "Airbus A321neo",
            "A20N" => "Airbus A320neo",
            "A19N" => "Airbus A319neo",
            "A319" => "Airbus A319",
            "A318" => "Airbus A318",
            "A359" => "Airbus A350-900",
            "A35K" => "Airbus A350-1000",
            "A332" => "Airbus A330-200",
            "A333" => "Airbus A330-300",
            "A339" => "Airbus A330-900neo",
            "B77W" => "Boeing 777-300ER",
            "B772" => "Boeing 777-200",
            "B77L" => "Boeing 777-200LR",
            "B788" => "Boeing 787-8 Dreamliner",
            "B789" => "Boeing 787-9 Dreamliner",
            "B78X" => "Boeing 787-10 Dreamliner",
            "B744" => "Boeing 747-400",
            "B748" => "Boeing 747-8",
            "E190" => "Embraer 190",
            "E195" => "Embraer 195",
            "E175" => "Embraer 175",
            "CRJ9" => "Bombardier CRJ-900",
            "CRJ2" => "Bombardier CRJ-200",
            "ATR7" => "ATR 72",
            "ATR4" => "ATR 42",
            "BCS1" => "Airbus A220-100",
            "BCS3" => "Airbus A220-300",
            _ => code
        };
    }

    private async Task<string> ConvertIataCallsignToIcaoAsync(string callsign)
    {
        if (string.IsNullOrEmpty(callsign) || callsign.Length < 3) return callsign;

        // Find the index of the first digit
        int firstDigitIndex = -1;
        for (int i = 0; i < callsign.Length; i++)
        {
            if (char.IsDigit(callsign[i]))
            {
                firstDigitIndex = i;
                break;
            }
        }

        if (firstDigitIndex < 2) return callsign;

        var iataCode = callsign[..firstDigitIndex];
        var flightNum = callsign[firstDigitIndex..];

        // If it's already 3-letter, it's likely ICAO
        if (iataCode.Length == 3) return callsign;

        // Look up IATA code in our DB
        var airline = await _db.Airlines
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.CodeIataAirline == iataCode);

        if (airline != null && !string.IsNullOrEmpty(airline.CodeIcaoAirline))
        {
            return $"{airline.CodeIcaoAirline}{flightNum}";
        }

        return callsign;
    }

    private static int? ExtractFlightNumber(string flightCode)
    {
        if (string.IsNullOrEmpty(flightCode)) return null;
        var digits = new string(flightCode.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var num) ? num : null;
    }

    private static string ExtractAirlineDesignator(string flightCode)
    {
        if (string.IsNullOrEmpty(flightCode)) return "";
        return new string(flightCode.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
    }

    private static string GetCountryFromRegistration(string reg)
    {
        if (string.IsNullOrEmpty(reg)) return "";
        
        reg = reg.ToUpperInvariant();
        
        if (reg.StartsWith("SU-")) return "Egypt";
        if (reg.StartsWith("OY-")) return "Denmark";
        if (reg.StartsWith("N")) return "United States";
        if (reg.StartsWith("G-")) return "United Kingdom";
        if (reg.StartsWith("VT-")) return "India";
        if (reg.StartsWith("D-")) return "Germany";
        if (reg.StartsWith("F-")) return "France";
        if (reg.StartsWith("EI-") || reg.StartsWith("EJ-")) return "Ireland";
        if (reg.StartsWith("TC-")) return "Turkey";
        if (reg.StartsWith("A6-")) return "United Arab Emirates";
        if (reg.StartsWith("HZ-")) return "Saudi Arabia";
        if (reg.StartsWith("B-")) return "China";
        if (reg.StartsWith("JA")) return "Japan";
        if (reg.StartsWith("HL")) return "South Korea";
        if (reg.StartsWith("VP-B") || reg.StartsWith("VQ-B")) return "Bermuda";
        if (reg.StartsWith("VH-")) return "Australia";
        if (reg.StartsWith("ZK-")) return "New Zealand";
        if (reg.StartsWith("C-")) return "Canada";
        if (reg.StartsWith("XA-") || reg.StartsWith("XB-") || reg.StartsWith("XC-")) return "Mexico";
        if (reg.StartsWith("PR-") || reg.StartsWith("PP-") || reg.StartsWith("PT-") || reg.StartsWith("PU-")) return "Brazil";
        if (reg.StartsWith("LV-")) return "Argentina";
        if (reg.StartsWith("EC-")) return "Spain";
        if (reg.StartsWith("I-")) return "Italy";
        if (reg.StartsWith("PH-")) return "Netherlands";
        if (reg.StartsWith("OO-")) return "Belgium";
        if (reg.StartsWith("HB-")) return "Switzerland";
        if (reg.StartsWith("OE-")) return "Austria";
        if (reg.StartsWith("CS-")) return "Portugal";
        if (reg.StartsWith("SE-")) return "Sweden";
        if (reg.StartsWith("LN-")) return "Norway";
        if (reg.StartsWith("OH-")) return "Finland";
        if (reg.StartsWith("SP-")) return "Poland";
        if (reg.StartsWith("UR-")) return "Ukraine";
        if (reg.StartsWith("RA-")) return "Russia";
        if (reg.StartsWith("4X-")) return "Israel";
        if (reg.StartsWith("9V-")) return "Singapore";
        if (reg.StartsWith("9M-")) return "Malaysia";
        if (reg.StartsWith("HS-")) return "Thailand";
        if (reg.StartsWith("VN-")) return "Vietnam";
        if (reg.StartsWith("PK-")) return "Indonesia";
        if (reg.StartsWith("AP-")) return "Pakistan";
        if (reg.StartsWith("ZS-")) return "South Africa";
        if (reg.StartsWith("CN-")) return "Morocco";
        if (reg.StartsWith("TS-")) return "Tunisia";
        if (reg.StartsWith("5A-")) return "Libya";
        if (reg.StartsWith("JY-")) return "Jordan";
        if (reg.StartsWith("OD-")) return "Lebanon";
        if (reg.StartsWith("YI-")) return "Iraq";
        if (reg.StartsWith("A4O-")) return "Oman";
        if (reg.StartsWith("A7-")) return "Qatar";
        if (reg.StartsWith("A9C-")) return "Bahrain";
        if (reg.StartsWith("9K-")) return "Kuwait";

        return "";
    }

    public async Task<Travora.Application.DTOs.Customer.Profile.SavedFlightsResponse> GetTrackedFlightsAsync(int? customerId, string? guestId)
    {
        IQueryable<SavedFlight> query = _db.SavedFlights
            .Include(sf => sf.Flight)
                .ThenInclude(f => f.DepartureAirport)
                    .ThenInclude(a => a.City)
            .Include(sf => sf.Flight)
                .ThenInclude(f => f.ArrivalAirport)
                    .ThenInclude(a => a.City)
            .Where(sf => sf.IsActive);

        if (customerId.HasValue)
        {
            query = query.Where(sf => sf.CustomerId == customerId.Value);
        }
        else if (!string.IsNullOrEmpty(guestId))
        {
            query = query.Where(sf => sf.GuestId == guestId && sf.CustomerId == null);
        }
        else
        {
            return new Travora.Application.DTOs.Customer.Profile.SavedFlightsResponse { Message = "No user or guest context provided" };
        }

        var savedFlights = await query.ToListAsync();
        if (!savedFlights.Any())
        {
            return new Travora.Application.DTOs.Customer.Profile.SavedFlightsResponse { Message = "No Flights Found" };
        }

        var dtos = savedFlights.Select(sf =>
        {
            var f = sf.Flight;
            string airlineLogoUrl = "";
            if (f != null)
            {
                var code = !string.IsNullOrEmpty(f.AirlineIataCode)
                    ? f.AirlineIataCode
                    : (!string.IsNullOrEmpty(f.FlightIataNumber) && System.Text.RegularExpressions.Regex.IsMatch(f.FlightIataNumber, @"^[A-Za-z]+")
                        ? System.Text.RegularExpressions.Regex.Match(f.FlightIataNumber, @"^[A-Za-z]+").Value
                        : (!string.IsNullOrEmpty(f.FlightNumber) && System.Text.RegularExpressions.Regex.IsMatch(f.FlightNumber, @"^[A-Za-z]+")
                            ? System.Text.RegularExpressions.Regex.Match(f.FlightNumber, @"^[A-Za-z]+").Value
                            : string.Empty));

                if (!string.IsNullOrEmpty(code))
                {
                    airlineLogoUrl = $"https://pics.avs.io/200/200/{code.ToUpper()}@2x.png";
                }
            }

            return new Travora.Application.DTOs.Customer.Profile.SavedFlightDto
            {
                SavedFlightId = sf.SavedFlightId,
                FlightNumber = f?.FlightNumber ?? string.Empty,
                FlightIcao = f?.FlightIcaoNumber ?? string.Empty,
                Registration = f?.AircraftRegistrationNumber ?? string.Empty,
                FromIata = f?.DepartureIataCode ?? string.Empty,
                ToIata = f?.ArrivalIataCode ?? string.Empty,
                DepartureCity = f?.DepartureAirport?.City?.NameCity ?? f?.DepartureAirport?.NameAirport ?? string.Empty,
                ArrivalCity = f?.ArrivalAirport?.City?.NameCity ?? f?.ArrivalAirport?.NameAirport ?? string.Empty,
                FlightDate = f?.ScheduledDepartureTime.ToString("dd MMM yyyy") ?? string.Empty,
                DepartureTime = f?.ScheduledDepartureTime.ToString("hh:mm tt") ?? string.Empty,
                ArrivalTime = f?.ScheduledArrivalTime.ToString("hh:mm tt") ?? string.Empty,
                Status = f?.FlightStatus.ToString() ?? "Scheduled",
                AirlineName = f?.AirlineName ?? string.Empty,
                AirlineLogoUrl = airlineLogoUrl,
                NotificationEnabled = sf.NotificationEnabled
            };
        }).ToList();

        return new Travora.Application.DTOs.Customer.Profile.SavedFlightsResponse { SavedFlights = dtos };
    }

    public async Task<(bool Success, string Message, int? SavedFlightId)> TrackFlightAsync(string flightIata, int? customerId, string? guestId)
    {
        flightIata = flightIata.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(flightIata))
        {
            return (false, "Flight IATA is required", null);
        }

        var details = await GetFlightDetailsAsync(flightIata);
        if (details == null)
        {
            return (false, $"Flight {flightIata} not found or no active live information available", null);
        }

        var flight = await _db.Flights.FirstOrDefaultAsync(f => f.FlightIataNumber == details.FlightIata || f.FlightNumber == details.FlightIata);
        if (flight == null)
        {
            var flightStatus = FlightStatus.InAir;
            var statusStr = details.Status.ToLowerInvariant();
            if (statusStr.Contains("landed") || statusStr.Contains("arrived"))
                flightStatus = FlightStatus.Landed;
            else if (statusStr.Contains("scheduled"))
                flightStatus = FlightStatus.Scheduled;
            else if (statusStr.Contains("cancelled"))
                flightStatus = FlightStatus.Cancelled;
            else if (statusStr.Contains("delayed"))
                flightStatus = FlightStatus.Delayed;
            else if (statusStr.Contains("boarding"))
                flightStatus = FlightStatus.Boarding;
            else if (statusStr.Contains("departed"))
                flightStatus = FlightStatus.Departed;

            var depAirport = await _db.Airports.FirstOrDefaultAsync(a => a.CodeIataAirport == details.Departure.Iata);
            var arrAirport = await _db.Airports.FirstOrDefaultAsync(a => a.CodeIataAirport == details.Arrival.Iata);

            DateTime depTime = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(details.Departure.ScheduledTime) && DateTime.TryParse(details.Departure.ScheduledTime, out var dt))
                depTime = dt;

            DateTime arrTime = DateTime.UtcNow.AddHours(2);
            if (!string.IsNullOrEmpty(details.Arrival.ScheduledTime) && DateTime.TryParse(details.Arrival.ScheduledTime, out var at))
                arrTime = at;

            flight = new Flight
            {
                FlightNumber = details.FlightIata,
                FlightIataNumber = details.FlightIata,
                FlightIcaoNumber = details.Aircraft.Registration ?? string.Empty,
                FlightStatus = flightStatus,
                DataSource = "AviationEdge",
                DepartureIataCode = details.Departure.Iata,
                ArrivalIataCode = details.Arrival.Iata,
                ScheduledDepartureTime = depTime,
                ScheduledArrivalTime = arrTime,
                AirlineName = details.Airline.Name,
                AirlineIataCode = details.Airline.Iata,
                AircraftRegistrationNumber = details.Aircraft.Registration,
                AircraftModelText = details.Aircraft.Model,
                AircraftModelCode = details.Aircraft.Type,
                DepartureAirportId = depAirport?.AirportId,
                ArrivalAirportId = arrAirport?.AirportId,
                CreatedAt = DateTime.UtcNow
            };

            _db.Flights.Add(flight);
            await _db.SaveChangesAsync();
        }

        SavedFlight? existing = null;
        if (customerId.HasValue)
        {
            existing = await _db.SavedFlights
                .FirstOrDefaultAsync(sf => sf.CustomerId == customerId.Value && sf.FlightId == flight.FlightId);
        }
        else if (!string.IsNullOrEmpty(guestId))
        {
            existing = await _db.SavedFlights
                .FirstOrDefaultAsync(sf => sf.GuestId == guestId && sf.CustomerId == null && sf.FlightId == flight.FlightId);
        }
        else
        {
            return (false, "User context or GuestId is required", null);
        }

        if (existing != null)
        {
            if (existing.IsActive)
            {
                return (true, "Flight is already being tracked", existing.SavedFlightId);
            }

            existing.IsActive = true;
            existing.NotificationEnabled = true;
            existing.SavedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (true, "Flight tracking reactivated", existing.SavedFlightId);
        }

        var newSavedFlight = new SavedFlight
        {
            CustomerId = customerId,
            GuestId = customerId.HasValue ? null : guestId,
            FlightId = flight.FlightId,
            IsActive = true,
            NotificationEnabled = true,
            SavedAt = DateTime.UtcNow
        };

        _db.SavedFlights.Add(newSavedFlight);
        await _db.SaveChangesAsync();

        return (true, "Flight tracked successfully", newSavedFlight.SavedFlightId);
    }

    public async Task<(bool Success, string Message)> RemoveTrackedFlightAsync(int savedFlightId, int? customerId, string? guestId)
    {
        SavedFlight? savedFlight = null;
        if (customerId.HasValue)
        {
            savedFlight = await _db.SavedFlights
                .FirstOrDefaultAsync(sf => sf.SavedFlightId == savedFlightId && sf.CustomerId == customerId.Value);
        }
        else if (!string.IsNullOrEmpty(guestId))
        {
            savedFlight = await _db.SavedFlights
                .FirstOrDefaultAsync(sf => sf.SavedFlightId == savedFlightId && sf.GuestId == guestId && sf.CustomerId == null);
        }

        if (savedFlight == null)
        {
            return (false, "Tracked flight not found or unauthorized");
        }

        savedFlight.IsActive = false;
        await _db.SaveChangesAsync();

        return (true, "Flight untracked successfully");
    }

    public async Task<(bool Success, string Message, bool? NotificationEnabled)> ToggleTrackedFlightNotificationAsync(int savedFlightId, int? customerId, string? guestId)
    {
        SavedFlight? savedFlight = null;
        if (customerId.HasValue)
        {
            savedFlight = await _db.SavedFlights
                .FirstOrDefaultAsync(sf => sf.SavedFlightId == savedFlightId && sf.CustomerId == customerId.Value && sf.IsActive);
        }
        else if (!string.IsNullOrEmpty(guestId))
        {
            savedFlight = await _db.SavedFlights
                .FirstOrDefaultAsync(sf => sf.SavedFlightId == savedFlightId && sf.GuestId == guestId && sf.CustomerId == null && sf.IsActive);
        }

        if (savedFlight == null)
        {
            return (false, "Tracked flight not found or unauthorized", null);
        }

        savedFlight.NotificationEnabled = !savedFlight.NotificationEnabled;
        await _db.SaveChangesAsync();

        return (true, "Notification state toggled successfully", savedFlight.NotificationEnabled);
    }

    public async Task<(bool Success, string Message)> MergeGuestTrackedFlightsAsync(string guestId, int customerId)
    {
        if (string.IsNullOrEmpty(guestId))
        {
            return (false, "GuestId is required");
        }

        var guestFlights = await _db.SavedFlights
            .Where(sf => sf.GuestId == guestId && sf.CustomerId == null && sf.IsActive)
            .ToListAsync();

        if (!guestFlights.Any())
        {
            return (true, "No guest flights to merge");
        }

        foreach (var gf in guestFlights)
        {
            var customerHasFlight = await _db.SavedFlights
                .AnyAsync(sf => sf.CustomerId == customerId && sf.FlightId == gf.FlightId && sf.IsActive);

            if (customerHasFlight)
            {
                gf.IsActive = false;
            }
            else
            {
                var customerSoftDeleted = await _db.SavedFlights
                    .FirstOrDefaultAsync(sf => sf.CustomerId == customerId && sf.FlightId == gf.FlightId && !sf.IsActive);

                if (customerSoftDeleted != null)
                {
                    customerSoftDeleted.IsActive = true;
                    customerSoftDeleted.NotificationEnabled = gf.NotificationEnabled;
                    customerSoftDeleted.SavedAt = gf.SavedAt;
                    gf.IsActive = false;
                }
                else
                {
                    gf.CustomerId = customerId;
                    gf.GuestId = null;
                }
            }
        }

        await _db.SaveChangesAsync();
        return (true, "Guest flights merged successfully");
    }
}

