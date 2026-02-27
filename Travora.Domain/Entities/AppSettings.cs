using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class AppSettings : IHasTimestamps
{
    public int SettingsId { get; set; }
    
    // General Settings
    public string CompanyName { get; set; } = "Travora";
    public string CompanyEmail { get; set; } = "info@travora.com";
    public string CompanyPhone { get; set; } = string.Empty;
    public string CompanyAddress { get; set; } = string.Empty;
    public string Timezone { get; set; } = "+2 GMT";
    public string Language { get; set; } = "English";

    // Tracking Settings
    public bool ShowEmployeeNamesOnMap { get; set; } = true;
    public bool AutoRefresh { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
