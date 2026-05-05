using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Admin.Passport;

public class ApprovePassportRequest
{
    [Required(ErrorMessage = "Passport number is required")]
    public string PassportNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nationality is required")]
    public string Nationality { get; set; } = string.Empty;

    [Required(ErrorMessage = "Gender is required")]
    public string Gender { get; set; } = string.Empty;

    [Required(ErrorMessage = "Date of birth is required")]
    public string DateOfBirth { get; set; } = string.Empty;

    [Required(ErrorMessage = "Expiry date is required")]
    public string ExpiryDate { get; set; } = string.Empty;
}
