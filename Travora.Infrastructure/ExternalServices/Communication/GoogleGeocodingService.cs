using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Travora.Infrastructure.Configurations;
using Travora.Application.DTOs.External.Geocoding;
using Travora.Application.Interfaces.External;

namespace Travora.Infrastructure.ExternalServices.Communication;

public class GoogleGeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;
    private readonly GeocodingSettings _settings;

    public GoogleGeocodingService(IHttpClientFactory httpClientFactory, IOptions<GeocodingSettings> settings)
    {
        _httpClient = httpClientFactory.CreateClient("GoogleGeocoding");
        _settings = settings.Value;
    }

    public async Task<ReverseGeocodingResponse?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var url = $"/maps/api/geocode/json?latlng={latitude},{longitude}&key={_settings.ApiKey}&language={_settings.Language}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var result = await response.Content.ReadFromJsonAsync<GoogleGeocodingApiResponse>(cancellationToken: cancellationToken);

        if (result == null || result.Status != "OK" || result.Results == null || result.Results.Count == 0)
            return null;

        var firstResult = result.Results[0];
        var components = firstResult.AddressComponents ?? new List<GoogleAddressComponent>();

        return new ReverseGeocodingResponse
        {
            Latitude = latitude,
            Longitude = longitude,
            FormattedAddress = firstResult.FormattedAddress ?? string.Empty,
            StreetAddress = BuildStreetAddress(components),
            Suburb = GetComponent(components, "sublocality", "sublocality_level_1", "neighborhood"),
            City = GetComponent(components, "locality", "administrative_area_level_2"),
            State = GetComponent(components, "administrative_area_level_1"),
            Country = GetComponent(components, "country"),
            PostalCode = GetComponent(components, "postal_code")
        };
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private static string? BuildStreetAddress(List<GoogleAddressComponent> components)
    {
        var streetNumber = GetComponent(components, "street_number");
        var route = GetComponent(components, "route");

        if (string.IsNullOrEmpty(route))
            return null;

        return string.IsNullOrEmpty(streetNumber) ? route : $"{streetNumber} {route}";
    }

    private static string? GetComponent(List<GoogleAddressComponent> components, params string[] types)
    {
        foreach (var type in types)
        {
            var component = components.FirstOrDefault(c => c.Types != null && c.Types.Contains(type));
            if (component != null)
                return component.LongName;
        }
        return null;
    }

    // ===================================================================
    // Google API Response Models (private)
    // ===================================================================

    private class GoogleGeocodingApiResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("results")]
        public List<GoogleGeocodingResult>? Results { get; set; }
    }

    private class GoogleGeocodingResult
    {
        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; set; }

        [JsonPropertyName("address_components")]
        public List<GoogleAddressComponent>? AddressComponents { get; set; }
    }

    private class GoogleAddressComponent
    {
        [JsonPropertyName("long_name")]
        public string? LongName { get; set; }

        [JsonPropertyName("short_name")]
        public string? ShortName { get; set; }

        [JsonPropertyName("types")]
        public List<string>? Types { get; set; }
    }
}
