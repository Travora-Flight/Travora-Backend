namespace Travora.API.Configurations;

public class AppSettings
{
    public AirlineApiSettings AirlineApi { get; set; } = new();
    public AviationEdgeSettings AviationEdge { get; set; } = new();
    public AviationWeatherSettings AviationWeather { get; set; } = new();
    public GeocodingSettings Geocoding { get; set; } = new();
    public PassportOcrSettings PassportOcr { get; set; } = new();
    public SeedSettings SeedSettings { get; set; } = new();
}

public class AirlineApiSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}

public class AviationEdgeSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}

public class AviationWeatherSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public int CacheTtlMinutes { get; set; } = 30;
}

public class GeocodingSettings
{
    public string Provider { get; set; } = "Nominatim";
    public string BaseUrl { get; set; } = string.Empty;
    public string UserAgent { get; set; } = "Travora/1.0";
}

public class PassportOcrSettings
{
    public double ConfidenceThreshold { get; set; } = 0.85;
    public double ManualReviewThreshold { get; set; } = 0.60;
}

public class SeedSettings
{
    public bool AutoSeedOnStartup { get; set; } = true;
    public string[] SeedOrder { get; set; } = ["countries", "cities", "airports", "airlines", "aircraft"];
}
