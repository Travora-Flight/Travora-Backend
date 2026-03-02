namespace Travora.Application.DTOs.Admin.Settings;

public class AppSettingsResponse
{
    public GeneralSettingsDto General { get; set; } = new();
    public TrackingSettingsDto Tracking { get; set; } = new();
}
