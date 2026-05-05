using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Travora.Application.DTOs.Customer.Auth;

public class RegisterStep2Request
{
    [Required(ErrorMessage = "Session ID is required")]
    public string SessionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username is required")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 30 characters")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username can only contain letters, numbers and _")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Date of birth is required")]
    public string DateOfBirth { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm password is required")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Passport image is required")]
    public IFormFile PassportImage { get; set; } = null!;

    [Required(ErrorMessage = "Passport expiry date is required")]
    public string PassportExpiryDate { get; set; } = string.Empty;
}
