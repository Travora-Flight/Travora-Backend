using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Customer.Auth;

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "الـ Reset Token مطلوب")]
    public string ResetToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة")]
    [MinLength(8, ErrorMessage = "كلمة المرور 8 أحرف على الأقل")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
