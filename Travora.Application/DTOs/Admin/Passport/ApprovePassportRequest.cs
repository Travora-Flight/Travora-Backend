using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Admin.Passport;

public class ApprovePassportRequest
{
    [Required(ErrorMessage = "رقم الجواز مطلوب")]
    public string PassportNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "الجنسية مطلوبة")]
    public string Nationality { get; set; } = string.Empty;

    [Required(ErrorMessage = "النوع مطلوب")]
    public string Gender { get; set; } = string.Empty;

    [Required(ErrorMessage = "تاريخ الميلاد مطلوب")]
    public string DateOfBirth { get; set; } = string.Empty;

    [Required(ErrorMessage = "تاريخ الانتهاء مطلوب")]
    public string ExpiryDate { get; set; } = string.Empty;
}
