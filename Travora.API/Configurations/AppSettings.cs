using Travora.Shared.Settings;
using Travora.Infrastructure.Configurations;

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
