using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Admin.Pricing;

public class PackageServiceItemRequest
{
    [Required]
    public int ServiceId { get; set; }
    [Required]
    public string Phase { get; set; } = string.Empty; // "pickup", "airport", "delivery"
    [Required]
    public bool IsFree { get; set; }
}

public class CreatePackageRequest
{
    [Required]
    public string PackageName { get; set; } = string.Empty;
    public decimal? Discount { get; set; }
    public bool IsActive { get; set; } = true;

    [Required]
    public int IncludedCompanions { get; set; }
    [Required]
    public decimal ExtraCompanionPrice { get; set; }
    public int? MaxCompanionsLimit { get; set; }

    [Required]
    public int IncludedBags { get; set; }
    [Required]
    public decimal ExtraBagPrice { get; set; }
    public int? MaxBagsLimit { get; set; }

    [Required]
    public string Description { get; set; } = string.Empty;

    public List<PackageServiceItemRequest> Services { get; set; } = new();
}

public class UpdatePackageRequest
{
    public string? PackageName { get; set; }
    public decimal? Discount { get; set; }
    public bool? IsActive { get; set; }

    public int? IncludedCompanions { get; set; }
    public decimal? ExtraCompanionPrice { get; set; }
    public int? MaxCompanionsLimit { get; set; }

    public int? IncludedBags { get; set; }
    public decimal? ExtraBagPrice { get; set; }
    public int? MaxBagsLimit { get; set; }

    public string? Description { get; set; }
    
    public List<PackageServiceItemRequest>? Services { get; set; }
}

public class CreatePackageResponse
{
    public int PackageId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public decimal FinalPrice { get; set; }
    public decimal? Discount { get; set; }
    public List<PackageServiceDetail> Services { get; set; } = new();
}
