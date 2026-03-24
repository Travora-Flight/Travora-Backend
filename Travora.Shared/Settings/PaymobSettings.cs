namespace Travora.Shared.Settings;

public class PaymobSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public int IntegrationId { get; set; }
    public int IframeId { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string HmacSecret { get; set; } = string.Empty;
}
