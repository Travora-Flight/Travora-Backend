using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Travora.Infrastructure.Configurations;
using Travora.Application.DTOs.External.Geocoding;
using Travora.Application.Interfaces.External;

namespace Travora.Infrastructure.ExternalServices.Communication;

public class NominatimGeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;

    public NominatimGeocodingService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("NominatimGeocoding");
    }

    public async Task<ReverseGeocodingResponse?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var url = $"/reverse?lat={latitude}&lon={longitude}&format=json&addressdetails=1";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<NominatimReverseResponse>(cancellationToken: cancellationToken);
        
        if (result == null)
        {
            return null;
        }

        var address = result.Address;

        return new ReverseGeocodingResponse
        {
            Latitude = latitude,
            Longitude = longitude,
            FormattedAddress = result.DisplayName ?? string.Empty,
            StreetAddress = string.Join(" ", new[]
            {
                address?.Road,
                address?.HouseNumber
            }.Where(x => !string.IsNullOrEmpty(x))),
            Suburb = address?.Suburb ?? address?.Neighbourhood ?? address?.Quarter,
            City = address?.City ?? address?.Town ?? address?.Village ?? address?.County,
            State = address?.State,
            Country = address?.Country,
            PostalCode = address?.Postcode
        };
    }

    private class NominatimReverseResponse
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("address")]
        public NominatimAddress? Address { get; set; }
    }

    private class NominatimAddress
    {
        [JsonPropertyName("road")]
        public string? Road { get; set; }

        [JsonPropertyName("house_number")]
        public string? HouseNumber { get; set; }

        [JsonPropertyName("suburb")]
        public string? Suburb { get; set; }

        [JsonPropertyName("neighbourhood")]
        public string? Neighbourhood { get; set; }

        [JsonPropertyName("quarter")]
        public string? Quarter { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }
        
        [JsonPropertyName("town")]
        public string? Town { get; set; }
        
        [JsonPropertyName("village")]
        public string? Village { get; set; }

        [JsonPropertyName("county")]
        public string? County { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("postcode")]
        public string? Postcode { get; set; }
    }
}
