namespace Travora.Infrastructure.Configurations;

public class GeocodingSettings
{
    public string Provider { get; set; } = "Google";
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Language { get; set; } = "ar";
    public string UserAgent { get; set; } = "Travora/1.0"; // kept for Nominatim fallback
}
