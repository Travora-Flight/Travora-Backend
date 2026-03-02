namespace Travora.API.Configurations;

public class AviationWeatherSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public int CacheTtlMinutes { get; set; } = 30;
}
