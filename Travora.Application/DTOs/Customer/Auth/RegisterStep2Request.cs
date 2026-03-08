using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Travora.Application.DTOs.Customer.Auth;

public class RegisterStep2Request
{
    [Required(ErrorMessage = "الـ Session ID مطلوب")]
    public string SessionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم المستخدم مطلوب")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "اسم المستخدم من 3 إلى 30 حرف")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "اسم المستخدم يحتوي فقط على حروف وأرقام و _")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "تاريخ الميلاد مطلوب")]
    public string DateOfBirth { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [MinLength(8, ErrorMessage = "كلمة المرور 8 أحرف على الأقل")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "صورة جواز السفر مطلوبة")]
    public IFormFile PassportImage { get; set; } = null!;

    [Required(ErrorMessage = "تاريخ انتهاء الجواز مطلوب")]
    public string PassportExpiryDate { get; set; } = string.Empty;
}
