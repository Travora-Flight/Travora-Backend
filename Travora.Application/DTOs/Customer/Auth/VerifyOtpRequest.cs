using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Customer.Auth;

public class VerifyOtpRequest
{
    [Required(ErrorMessage = "الإيميل مطلوب")]
    [EmailAddress(ErrorMessage = "الإيميل غير صحيح")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "كود التحقق مطلوب")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "كود التحقق 6 أرقام")]
    public string Otp { get; set; } = string.Empty;
}
