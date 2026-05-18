using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Travora.Application.DTOs.Customer.Auth;

public class RegisterStep2Request
{
    [Required(ErrorMessage = "Session ID is required")]
    public string SessionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Passport image is required")]
    public IFormFile PassportImage { get; set; } = null!;

    [Required(ErrorMessage = "Passport expiry date is required")]
    public string PassportExpiryDate { get; set; } = string.Empty;

    [Required(ErrorMessage = "Passport number is required")]
    public string PassportNumber { get; set; } = string.Empty;

    public bool? ForceSubmit { get; set; }
}
