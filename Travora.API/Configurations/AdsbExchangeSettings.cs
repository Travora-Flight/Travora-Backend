namespace Travora.API.Configurations;

/// <summary>
/// Configuration for ADSBexchange API accessed via RapidAPI proxy.
/// API Key and Host are stored in appsettings.json — never hardcoded.
/// </summary>
public class AdsbExchangeSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string RapidApiKey { get; set; } = string.Empty;
    public string RapidApiHost { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Maximum radius in nautical miles for viewport queries (API limit: 250 NM).
    /// </summary>
    public int MaxRadiusNm { get; set; } = 250;
}
