using Travora.Application.DTOs.External.Geocoding;
using Travora.Application.Interfaces.External;

namespace Travora.Infrastructure.ExternalServices.Communication;

/// <summary>
/// Tries Google Geocoding first, falls back to Nominatim if Google fails.
/// </summary>
public class FallbackGeocodingService : IGeocodingService
{
    private readonly GoogleGeocodingService _google;
    private readonly NominatimGeocodingService _nominatim;

    public FallbackGeocodingService(GoogleGeocodingService google, NominatimGeocodingService nominatim)
    {
        _google = google;
        _nominatim = nominatim;
    }

    public async Task<ReverseGeocodingResponse?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        // Try Google first
        try
        {
            var result = await _google.ReverseGeocodeAsync(latitude, longitude, cancellationToken);
            if (result != null && !string.IsNullOrEmpty(result.FormattedAddress))
                return result;
        }
        catch
        {
            // Google failed, fall through to Nominatim
        }

        // Fallback to Nominatim
        return await _nominatim.ReverseGeocodeAsync(latitude, longitude, cancellationToken);
    }
}
