namespace Travora.Application.DTOs.Admin.Pricing;

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
