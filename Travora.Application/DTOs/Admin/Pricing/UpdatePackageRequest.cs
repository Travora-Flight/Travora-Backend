namespace Travora.Application.DTOs.Admin.Pricing;

public class UpdatePackageRequest
{
    public string? PackageName { get; set; }
    public decimal? Discount { get; set; }
    public int? IncludedCompanions { get; set; }
    public decimal? ExtraCompanionPrice { get; set; }
    public int? MaxCompanionsLimit { get; set; }

    public int? IncludedBags { get; set; }
    public decimal? ExtraBagPrice { get; set; }
    public int? MaxBagsLimit { get; set; }

    public string? Description { get; set; }
    
    public List<PackageServiceItemRequest>? Services { get; set; }
}
