namespace Travora.Application.DTOs.Employee.Account;

public class EmployeeProfileResponse
{
    public string? ProfileImageUrl { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string JobRole { get; set; } = string.Empty;
    public string ShiftType { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NationalId { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? CheckPoint { get; set; }
    public string? PlateNumber { get; set; }
}
