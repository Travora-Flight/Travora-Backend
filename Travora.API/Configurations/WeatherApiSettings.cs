namespace Travora.API.Configurations;

public class WeatherApiSettings
{
    public string BaseUrl { get; set; } = "https://api.weatherapi.com/v1";
    public string ApiKey { get; set; } = string.Empty;
    public int CacheTtlMinutes { get; set; } = 30;
}
