using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Service : IHasTimestamps
{
    public int ServiceId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public ServiceType ServiceType { get; set; }
    public decimal BasePrice { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<PackageService> PackageServices { get; set; } = new List<PackageService>();
}
