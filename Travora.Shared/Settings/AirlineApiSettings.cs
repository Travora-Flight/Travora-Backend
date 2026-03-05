namespace Travora.Shared.Settings;

public class AirlineApiSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}
