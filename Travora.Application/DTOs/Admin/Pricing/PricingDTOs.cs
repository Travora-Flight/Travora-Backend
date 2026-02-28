namespace Travora.Application.DTOs.Admin.Pricing;

public class PricingStatsResponse
{
    public int TotalServices { get; set; }
    public int TotalPackages { get; set; }
    public int ActiveServices { get; set; }
}

public class ServiceDetailResponse
{
    public int ServiceId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool IsActive { get; set; }
}

public class PackageServiceDetail
{
    public string Name { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public bool IsFree { get; set; }
}

public class PackageDetailResponse
{
    public int PackageId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal FinalPrice { get; set; }
    public decimal? Discount { get; set; }
    public string Currency { get; set; } = "EGP";
    public int IncludedCompanions { get; set; }
    public decimal ExtraCompanionPrice { get; set; }
    public int IncludedBags { get; set; }
    public decimal ExtraBagPrice { get; set; }
    public List<PackageServiceDetail> ServicesIncluded { get; set; } = new();
}

public class PricingOverviewResponse
{
    public PricingStatsResponse Stats { get; set; } = new();
    public List<ServiceDetailResponse> Services { get; set; } = new();
    public List<PackageDetailResponse> Packages { get; set; } = new();
}

public class PublicPricingStatsResponse
{
    public int ActiveServices { get; set; }
    public int ActivePackages { get; set; }
}
