using Microsoft.AspNetCore.Http;

namespace Travora.Application.DTOs.Employee.Account;

public class UpdateProfileRequest
{
    public string? MobileNumber { get; set; }
    public string? Address { get; set; }
    public IFormFile? ProfilePhoto { get; set; }
}
