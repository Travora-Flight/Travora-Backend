namespace Travora.Application.DTOs.Admin.Settings;

public class UpdateGeneralSettingsRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
}
