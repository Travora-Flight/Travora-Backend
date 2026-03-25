namespace Travora.Application.DTOs.Customer.Profile;

public class CustomerSettingsResponse
{
    public bool NotificationsEnabled { get; set; } = true;
    public string Language { get; set; } = "English";
}

public class CustomerSettingsRequest
{
    public bool NotificationsEnabled { get; set; }
    public string Language { get; set; } = string.Empty;
}
