using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Customer.Auth;

public class RegisterStep1Request
{
    [Required(ErrorMessage = "الاسم الأول مطلوب")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "الاسم الأول من 2 إلى 50 حرف")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "الاسم الأخير مطلوب")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "الاسم الأخير من 2 إلى 50 حرف")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "الإيميل مطلوب")]
    [EmailAddress(ErrorMessage = "الإيميل غير صحيح")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [RegularExpression(@"^\d{11}$", ErrorMessage = "رقم الهاتف يجب أن يتكون من 11 رقم")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "الجنسية مطلوبة")]
    public string Nationality { get; set; } = string.Empty;

    [Required(ErrorMessage = "النوع مطلوب")]
    public string Gender { get; set; } = string.Empty;
}
