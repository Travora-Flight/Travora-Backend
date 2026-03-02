using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Admin.Pricing;

public class CreateServiceRequest
{
    [Required]
    public string ServiceName { get; set; } = string.Empty;
    [Required]
    public decimal BasePrice { get; set; }
    [Required]
    public string Type { get; set; } = string.Empty;
    [Required]
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
