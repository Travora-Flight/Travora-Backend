namespace Travora.Shared.Settings;

public class PaymobSettings
{
    /// <summary>Secret key for Intention API authentication (e.g. egy_sk_test_...).</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Legacy API key — kept for backward compatibility during migration.</summary>
    public string ApiKey { get; set; } = string.Empty;
    public int IntegrationId { get; set; }
    public int IframeId { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string HmacSecret { get; set; } = string.Empty;

    /// <summary>Webhook URL sent with each intention so Paymob knows where to post callbacks.</summary>
    public string NotificationUrl { get; set; } = string.Empty;
}
