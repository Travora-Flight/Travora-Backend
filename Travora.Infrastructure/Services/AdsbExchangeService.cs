using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Travora.Application.DTOs.Flights.Tracker;
using Travora.Application.Interfaces.Services;

namespace Travora.Infrastructure.Services;

/// <summary>
/// Concrete implementation of <see cref="IAdsbExchangeService"/>.
/// Communicates with ADSBexchange via RapidAPI proxy.
///
/// Design decisions:
/// - Uses IHttpClientFactory (named client "AdsbExchange") to avoid socket exhaustion.
/// - API key is injected from configuration — never hardcoded.
/// - All parsing is defensive: malformed fields are silently skipped, not thrown.
/// - Returns empty collections on failure — the caller (FlightTrackerService) handles fallback.
/// </summary>
public class AdsbExchangeService : IAdsbExchangeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AdsbExchangeService> _logger;
    private readonly int _maxRadiusNm;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AdsbExchangeService(
        IHttpClientFactory httpClientFactory,
        ILogger<AdsbExchangeService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _maxRadiusNm = int.TryParse(configuration["AdsbExchange:MaxRadiusNm"], out var max) ? max : 250;
    }

    /// <inheritdoc />
    public async Task<List<AdsbAircraftDto>> GetAircraftInRadiusAsync(double lat, double lon, int radiusNm)
    {
        // Clamp radius to API limit
        radiusNm = Math.Clamp(radiusNm, 1, _maxRadiusNm);

        var latStr = lat.ToString("F4", CultureInfo.InvariantCulture);
        var lonStr = lon.ToString("F4", CultureInfo.InvariantCulture);
        var url = $"v2/lat/{latStr}/lon/{lonStr}/dist/{radiusNm}/";

        return await FetchAircraftListAsync(url);
    }

    /// <inheritdoc />
    public async Task<AdsbAircraftDto?> GetAircraftByCallsignAsync(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return null;

        var sanitized = SanitizePathSegment(callsign);
        var url = $"v2/callsign/{sanitized}/";

        var results = await FetchAircraftListAsync(url);
        return results.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<AdsbAircraftDto?> GetAircraftByIcaoAsync(string icaoHex)
    {
        if (string.IsNullOrWhiteSpace(icaoHex)) return null;

        var sanitized = SanitizePathSegment(icaoHex);
        var url = $"v2/icao/{sanitized}/";

        var results = await FetchAircraftListAsync(url);
        return results.FirstOrDefault();
    }

    // ===================================================================
    // Internal helpers
    // ===================================================================

    /// <summary>
    /// Core method: sends GET to ADSBexchange, parses the "ac" array from JSON.
    /// </summary>
    private async Task<List<AdsbAircraftDto>> FetchAircraftListAsync(string relativeUrl)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("AdsbExchange");
            var response = await client.GetAsync(relativeUrl);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "ADSBexchange API returned {StatusCode} for {Url}",
                    (int)response.StatusCode, relativeUrl);
                return new List<AdsbAircraftDto>();
            }

            var json = await response.Content.ReadAsStringAsync();
            return ParseAircraftResponse(json);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("ADSBexchange API request timed out for {Url}", relativeUrl);
            return new List<AdsbAircraftDto>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ADSBexchange API network error for {Url}", relativeUrl);
            return new List<AdsbAircraftDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling ADSBexchange API for {Url}", relativeUrl);
            return new List<AdsbAircraftDto>();
        }
    }

    /// <summary>
    /// Parses the raw JSON response from ADSBexchange into a list of DTOs.
    /// Response format: { "ac": [ { ... }, { ... } ], "total": N, "now": ..., ... }
    /// </summary>
    private static List<AdsbAircraftDto> ParseAircraftResponse(string json)
    {
        var result = new List<AdsbAircraftDto>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ac", out var acArray) || acArray.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var ac in acArray.EnumerateArray())
        {
            var dto = ParseSingleAircraft(ac);
            if (dto != null)
                result.Add(dto);
        }

        return result;
    }

    /// <summary>
    /// Parses a single aircraft JSON object into an <see cref="AdsbAircraftDto"/>.
    /// Returns null if the aircraft has no valid position data.
    /// </summary>
    private static AdsbAircraftDto? ParseSingleAircraft(JsonElement ac)
    {
        // Must have lat/lon to be useful for the map
        var lat = GetDecimal(ac, "lat");
        var lon = GetDecimal(ac, "lon");
        if (lat == 0 && lon == 0) return null;

        var hex = GetString(ac, "hex");
        if (string.IsNullOrEmpty(hex)) return null;

        // alt_baro can be a number OR the string "ground"
        decimal altBaro = 0;
        bool isOnGround = false;

        if (ac.TryGetProperty("alt_baro", out var altProp))
        {
            if (altProp.ValueKind == JsonValueKind.String)
            {
                var altStr = altProp.GetString();
                if (string.Equals(altStr, "ground", StringComparison.OrdinalIgnoreCase))
                {
                    isOnGround = true;
                    altBaro = 0;
                }
                else
                {
                    decimal.TryParse(altStr, NumberStyles.Any, CultureInfo.InvariantCulture, out altBaro);
                }
            }
            else if (altProp.ValueKind == JsonValueKind.Number)
            {
                altProp.TryGetDecimal(out altBaro);
            }
        }

        return new AdsbAircraftDto
        {
            Hex = hex,
            Callsign = GetString(ac, "flight").Trim(),
            Registration = GetString(ac, "r"),
            AircraftType = GetString(ac, "t"),
            Lat = lat,
            Lon = lon,
            AltitudeFt = altBaro,
            SpeedKts = GetDecimal(ac, "gs"),
            Heading = GetDecimal(ac, "track"),
            IsOnGround = isOnGround,
            Squawk = GetString(ac, "squawk"),
            Emergency = GetString(ac, "emergency"),
            SeenSeconds = GetDouble(ac, "seen"),
            Category = GetString(ac, "category")
        };
    }

    // ===================================================================
    // Safe JSON accessors — never throw
    // ===================================================================

    private static string GetString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static decimal GetDecimal(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number && val.TryGetDecimal(out var d)) return d;
            if (decimal.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return 0;
    }

    private static double GetDouble(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out var d)) return d;
            if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return 0;
    }

    /// <summary>
    /// Sanitizes a path segment to prevent path traversal or injection attacks.
    /// Only allows alphanumeric characters, hyphens, and underscores.
    /// </summary>
    private static string SanitizePathSegment(string input)
    {
        var trimmed = input.Trim().ToUpperInvariant();
        // Allow only safe characters: letters, digits, hyphen
        return new string(trimmed.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
    }
}
