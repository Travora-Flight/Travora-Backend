using Travora.Application.DTOs.External.Geocoding;

namespace Travora.Application.Interfaces.External;

public interface IGeocodingService
{
    Task<ReverseGeocodingResponse?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
