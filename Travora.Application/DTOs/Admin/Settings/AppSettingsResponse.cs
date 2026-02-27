namespace Travora.Application.DTOs.Admin.Settings;

public class AppSettingsResponse
{
    public GeneralSettingsDto General { get; set; } = new();
    public TrackingSettingsDto Tracking { get; set; } = new();
}

public class GeneralSettingsDto
{
    public string CompanyName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
}

public class TrackingSettingsDto
{
    public bool ShowEmployeeNamesOnMap { get; set; }
    public bool AutoRefresh { get; set; }
}
