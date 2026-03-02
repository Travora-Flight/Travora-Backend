using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Admin.Pricing;

public class PackageServiceItemRequest
{
    [Required]
    public int ServiceId { get; set; }
    [Required]
    public string Phase { get; set; } = string.Empty;
    [Required]
    public bool IsFree { get; set; }
}
