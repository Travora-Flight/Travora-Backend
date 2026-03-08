using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Customer.Auth;

public class CustomerLoginRequest
{
    [Required(ErrorMessage = "الإيميل مطلوب")]
    [EmailAddress(ErrorMessage = "الإيميل غير صحيح")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    public string Password { get; set; } = string.Empty;
}
