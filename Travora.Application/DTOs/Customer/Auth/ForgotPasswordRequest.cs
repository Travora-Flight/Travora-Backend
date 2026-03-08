using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Customer.Auth;

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "الإيميل مطلوب")]
    [EmailAddress(ErrorMessage = "الإيميل غير صحيح")]
    public string Email { get; set; } = string.Empty;
}
