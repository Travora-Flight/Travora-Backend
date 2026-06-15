using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    private readonly IAdsbExchangeService _adsbService;
    private readonly ILogger<FlightTrackerService> _logger;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    private const string LiveFlightsCacheKey = "flights:live:all";
    private const string LiveFlightsTimestampKey = "flights:live:timestamp";
    private const string TimetableCachePrefix = "timetable:";

    // ADSB data updates every ~2s — cache for 8s to align with the mobile app's 10s polling interval
    private static readonly TimeSpan AdsbCacheTtl = TimeSpan.FromSeconds(8);
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
        bool isZoomedIn = false, decimal? centerLat = null, decimal? centerLng = null, int? distance = null)
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
            distance = (int)Math.Clamp(diagonalNm / 2, 5, 250);
        }

        // ----- Step 2: Check ADSB cache first -----
        var adsbCacheKey = $"adsb:viewport:{cLat:F1}:{cLon:F1}:{distance}";
        var cached = await SafeCacheGet(adsbCacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            var cachedResult = JsonSerializer.Deserialize<ViewportFlightsResponse>(cached, JsonOptions);
            if (cachedResult != null)
                return cachedResult;
        }

        // ----- Step 3: Try ADSBexchange as primary source -----
        List<ViewportFlightDto>? resultFlights = null;
        long lastApiUpdate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string dataSource = "adsb";

        try
        {
            var adsbResults = await _adsbService.GetAircraftInRadiusAsync(
                (double)cLat, (double)cLon, distance.Value);

            if (adsbResults.Count > 0)
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
                        Reg = a.Registration,
                        Dep = string.Empty,  // ADSB doesn't provide departure/arrival
                        Arr = string.Empty
                    })
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ADSBexchange primary source failed, falling back to Aviation Edge");
        }

        // ----- Step 4: Fallback to Aviation Edge if ADSB returned nothing -----
        if (resultFlights == null || resultFlights.Count == 0)
        {
            dataSource = "aviation-edge";
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

        // ----- Step 5: Cache the result -----
        var cacheTtl = dataSource == "adsb" ? AdsbCacheTtl : GlobalCacheTtl;
        await SafeCacheSet(adsbCacheKey, JsonSerializer.Serialize(response), cacheTtl);

        return response;
    }

    /// <summary>
    /// Fallback: fetches viewport flights from Aviation Edge (original logic).
    /// Only called when ADSBexchange is down or returns no data.
    /// </summary>
    private async Task<List<ViewportFlightDto>> FetchAviationEdgeViewportAsync(
        decimal minLat, decimal maxLat, decimal minLng, decimal maxLng,
        decimal centerLat, decimal centerLon, int distanceKm)
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
                Airline = f.Airline, Reg = f.Reg, Dep = f.Dep, Arr = f.Arr
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
                var client = _httpClientFactory.CreateClient("AviationEdge");
                var searchIata = await ConvertIcaoCallsignToIataAsync(q.ToUpper());
                string url = q.Length == 2 && q.All(char.IsLetter)
                    ? $"{_baseUrl}/flights?key={_apiKey}&airlineIata={q.ToUpper()}&limit=10"
                    : $"{_baseUrl}/flights?key={_apiKey}&flightIata={Uri.EscapeDataString(searchIata)}";

                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!json.TrimStart().StartsWith("{"))
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
                                    ArrivalIata = GetNestedString(f, "arrival", "iataCode")
                                };
                            })
                            .Where(f => !string.IsNullOrEmpty(f.FlightIata)));
                        }
                    }
                }
            }
            catch { }
        }

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
            // If the key is exactly 6 alphanumeric characters, treat it as ICAO Hex ID
            if (flightIata.Length == 6 && flightIata.All(char.IsLetterOrDigit))
            {
                adsbLive = await _adsbService.GetAircraftByIcaoAsync(flightIata);
            }
            else
            {
                adsbLive = await _adsbService.GetAircraftByCallsignAsync(flightIata);
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
                Reg = adsbLive.Registration
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
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (!json.TrimStart().StartsWith("{"))
                {
                    var flights = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions);
                    if (flights != null && flights.Count > 0)
                    {
                        rawFlight = flights[0];
                        var f = rawFlight.Value;
                        var lastPos = GetLastPosition(f);

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
                                Arr = GetNestedString(f, "arrival", "iataCode")
                            };
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
                        }

                        trail = ExtractFlightTrail(f);
                    }
                }
            }
        }
        catch { }

        // If we still have no liveData, we cannot return flight details
        if (liveData == null) return null;

        TimetableData? timetable = null;
        if (!string.IsNullOrEmpty(liveData.Dep))
        {
            timetable = await GetTimetableDataAsync(liveData.Dep, targetFlight);
        }

        var depAirport = await _db.Airports.Include(a => a.City).FirstOrDefaultAsync(a => a.CodeIataAirport == liveData.Dep);
        var arrAirport = await _db.Airports.Include(a => a.City).FirstOrDefaultAsync(a => a.CodeIataAirport == liveData.Arr);
        var airline = await _db.Airlines.FirstOrDefaultAsync(a => a.CodeIataAirline == liveData.Airline || a.CodeIcaoAirline == liveData.Airline);
        var aircraft = !string.IsNullOrEmpty(liveData.Reg)
            ? await _db.Aircrafts.FirstOrDefaultAsync(a => a.NumberRegistration == liveData.Reg)
            : null;

        var rawScheduledDep = rawFlight.HasValue ? GetNestedString(rawFlight.Value, "departure", "scheduledTime") : null;
        var rawScheduledArr = rawFlight.HasValue ? GetNestedString(rawFlight.Value, "arrival", "scheduledTime") : null;

        var scheduledDep = ParseTimeFromScheduled(rawScheduledDep);
        var scheduledArr = ParseTimeFromScheduled(rawScheduledArr);

        var result = new FlightDetailsResponse
        {
            FlightIata = liveData.FlightIata,
            Status = liveData.Sts,
            DelayMessage = BuildDelayMessage(timetable?.DepartureDelay),
            Airline = new FlightDetailAirlineDto
            {
                Name = airline?.NameAirline ?? timetable?.AirlineName ?? liveData.Airline,
                Iata = airline?.CodeIataAirline ?? liveData.Airline,
                Logo = airline?.LogoUrl
            },
            Aircraft = new AircraftInfo
            {
                Registration = liveData.Reg,
                Model = aircraft?.ProductionLine ?? aircraft?.PlaneModel ?? GetAircraftModelFromApi(rawFlight)
            },
            Departure = new FlightDetailAirportDto
            {
                Iata = liveData.Dep,
                Name = depAirport?.NameAirport ?? "",
                City = depAirport?.City?.NameCity ?? "",
                Utc = FormatUtc(depAirport?.GMT),
                Gate = timetable?.DepartureGate,
                Terminal = timetable?.DepartureTerminal,
                ScheduledTime = scheduledDep,
                ActualTime = timetable?.ActualDeparture ?? "",
                Delay = timetable?.DepartureDelay
            },
            Arrival = new FlightDetailAirportDto
            {
                Iata = liveData.Arr,
                Name = arrAirport?.NameAirport ?? "",
                City = arrAirport?.City?.NameCity ?? "",
                Utc = FormatUtc(arrAirport?.GMT),
                Gate = timetable?.ArrivalGate,
                Terminal = timetable?.ArrivalTerminal,
                ScheduledTime = scheduledArr,
                EstimatedTime = timetable?.EstimatedArrival ?? scheduledArr
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

    public async Task<TimetableResponse> GetAirportTimetableAsync(string airportCode, string type = "departure")
    {
        var cacheKey = $"{TimetableCachePrefix}{airportCode}:{type}";
        var result = new TimetableResponse
        {
            AirportIata = airportCode,
            Type = type
        };

        var airport = await _db.Airports.FirstOrDefaultAsync(a => a.CodeIataAirport == airportCode);
        if (airport != null) result.AirportName = airport.NameAirport;

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
                var url = $"{_baseUrl}/timetable?key={_apiKey}&iataCode={Uri.EscapeDataString(airportCode)}&type={type}";
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

            if (timetableFlights != null)
            {
                var dtoList = new List<TimetableFlightDto>();
                foreach (var f in timetableFlights.Take(50))
                {
                    var dto = new TimetableFlightDto
                    {
                        FlightIata = GetNestedString(f, "flight", "iataNumber"),
                        AirlineName = GetNestedString(f, "airline", "name"),
                        AirlineIata = GetNestedString(f, "airline", "iataCode"),
                        DepartureIata = GetNestedString(f, "departure", "iataCode"),
                        DepartureGate = GetNestedStringOrNull(f, "departure", "gate"),
                        DepartureTerminal = GetNestedStringOrNull(f, "departure", "terminal"),
                        DepartureScheduledTime = ParseTimeFromScheduled(GetNestedString(f, "departure", "scheduledTime")),
                        DepartureEstimatedTime = ParseTimeFromScheduled(GetNestedString(f, "departure", "estimatedTime")),
                        DepartureActualTime = ParseTimeFromScheduled(GetNestedString(f, "departure", "actualTime")),
                        DepartureDelay = GetNestedNullableInt(f, "departure", "delay"),
                        ArrivalIata = GetNestedString(f, "arrival", "iataCode"),
                        ArrivalGate = GetNestedStringOrNull(f, "arrival", "gate"),
                        ArrivalTerminal = GetNestedStringOrNull(f, "arrival", "terminal"),
                        ArrivalScheduledTime = ParseTimeFromScheduled(GetNestedString(f, "arrival", "scheduledTime")),
                        ArrivalEstimatedTime = ParseTimeFromScheduled(GetNestedString(f, "arrival", "estimatedTime")),
                        Status = GetString(f, "status")
                    };

                    if (!string.IsNullOrEmpty(dto.FlightIata))
                        dtoList.Add(dto);
                }
                result.Flights = dtoList;
                result.Count = dtoList.Count;
            }
        }
        catch { }

        if (result.Flights.Any())
        {
            var airlineIatas = result.Flights.Select(f => f.AirlineIata).Distinct().ToList();
            var airlines = await _db.Airlines.Where(a => airlineIatas.Contains(a.CodeIataAirline)).ToDictionaryAsync(a => a.CodeIataAirline);
            foreach (var f in result.Flights)
            {
                if (airlines.TryGetValue(f.AirlineIata, out var matchedAirline))
                {
                    f.AirlineLogoUrl = matchedAirline.LogoUrl;
                }
            }
        }

        return result;
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

            var match = timetableFlights.FirstOrDefault(f =>
            {
                var iata = GetNestedString(f, "flight", "iataNumber");
                return iata.Equals(flightIata, StringComparison.OrdinalIgnoreCase);
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

        if (DateTime.TryParse(scheduledTime, out var dt))
            return dt.ToString("HH:mm");

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
