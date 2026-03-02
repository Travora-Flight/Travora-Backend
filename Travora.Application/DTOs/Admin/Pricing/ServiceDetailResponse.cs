namespace Travora.Application.DTOs.Admin.Pricing;

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
