using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Admin.Pricing;

public class PackageServiceItemRequest
{
    [Required]
    public int ServiceId { get; set; }

    [DefaultValue(false)]
    public bool IsFree { get; set; } = false;
}
