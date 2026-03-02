using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class Package : IHasTimestamps, ISoftDelete
{
    public int PackageId { get; set; }
    public string PackageCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Pricing - Companions
    public decimal TotalBasePrice { get; set; }
    public int IncludedCompanionsCount { get; set; } = 1;
    public decimal ExtraCompanionPrice { get; set; }
    public int? MaxCompanionsLimit { get; set; }

    // Pricing - Baggage
    public int IncludedBaggageCount { get; set; } = 2;
    public decimal ExtraBaggagePrice { get; set; }
    public int? MaxBaggageLimit { get; set; }

    public decimal? Discount { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<PackageService> PackageServices { get; set; } = new List<PackageService>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
