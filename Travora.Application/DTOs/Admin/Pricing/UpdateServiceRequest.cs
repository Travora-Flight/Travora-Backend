namespace Travora.Application.DTOs.Admin.Pricing;

public class UpdateServiceRequest
{
    public string? ServiceName { get; set; }
    public decimal? BasePrice { get; set; }
    public string? Type { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}
