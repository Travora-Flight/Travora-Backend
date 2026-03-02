namespace Travora.Application.DTOs.Admin.Pricing;

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
