namespace Travora.Application.DTOs.Admin.Pricing;

public class ServicePricingItem
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
}

public class PackagePricingItem
{
    public int PackageId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public decimal TotalBasePrice { get; set; }
    public decimal ExtraBaggagePrice { get; set; }
    public decimal ExtraCompanionPrice { get; set; }
}

public class PricingOverviewResponse
{
    public List<ServicePricingItem> Services { get; set; } = new();
    public List<PackagePricingItem> Packages { get; set; } = new();
}

public class UpdateServicePriceRequest
{
    public decimal NewBasePrice { get; set; }
}

public class UpdatePackagePricingRequest
{
    public decimal NewExtraBaggagePrice { get; set; }
    public decimal NewExtraCompanionPrice { get; set; }
}
