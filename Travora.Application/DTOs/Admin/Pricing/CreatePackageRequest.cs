using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Admin.Pricing;

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
