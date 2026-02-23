using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class PackageService : IHasTimestamps
{
    public int PackageServiceId { get; set; }
    public bool IncludedInBase { get; set; }
    public ExecutionPhase ExecutionPhase { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int PackageId { get; set; }
    public int ServiceId { get; set; }

    // Navigation properties
    public Package Package { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public ICollection<OrderService> OrderServices { get; set; } = new List<OrderService>();
}
