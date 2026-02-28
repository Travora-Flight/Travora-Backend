using System.ComponentModel.DataAnnotations;
using Travora.Domain.Enums;

namespace Travora.Application.DTOs.Admin.Pricing;

public class CreateServiceRequest
{
    [Required]
    public string ServiceName { get; set; } = string.Empty;
    [Required]
    public decimal BasePrice { get; set; }
    [Required]
    public string Type { get; set; } = string.Empty; // "pickup", "delivery", "tracking"
    [Required]
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class UpdateServiceRequest
{
    public string? ServiceName { get; set; }
    public decimal? BasePrice { get; set; }
    public string? Type { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}
