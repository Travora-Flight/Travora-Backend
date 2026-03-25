namespace Travora.Application.DTOs.Admin.Settings;

public class UpdateGeneralSettingsRequest
{
    public string? CompanyName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Timezone { get; set; }
    public string? Language { get; set; }
}
