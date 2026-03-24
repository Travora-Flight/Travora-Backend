namespace Travora.Infrastructure.Configurations;

public class GeocodingSettings
{
    public string Provider { get; set; } = "Nominatim";
    public string BaseUrl { get; set; } = string.Empty;
    public string UserAgent { get; set; } = "Travora/1.0";
}
