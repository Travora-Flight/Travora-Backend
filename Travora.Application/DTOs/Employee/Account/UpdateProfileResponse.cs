namespace Travora.Application.DTOs.Employee.Account;

public class UpdateProfileResponse
{
    public bool Success { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? MobileNumber { get; set; }
    public string? Address { get; set; }
}
