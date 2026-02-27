namespace Travora.Application.DTOs.Admin.Employees;

public class EmployeeProfileResponse
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string JobRole { get; set; } = string.Empty;
    public string ProfileImageUrl { get; set; } = string.Empty;
    public string? NationalIdImageUrl { get; set; }
    public string? DriverLicenseUrl { get; set; }
    
    public EmployeeContactInfo ContactInfo { get; set; } = new();
    public EmployeeAdditionalDetails AdditionalDetails { get; set; } = new();
    
    public int? VehicleId { get; set; }
    public int? CheckpointId { get; set; }
}

public class EmployeeContactInfo
{
    public string Email { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
}

public class EmployeeAdditionalDetails
{
    public string DateOfBirth { get; set; } = string.Empty;
    public string ShiftType { get; set; } = string.Empty;
    public string NationalId { get; set; } = string.Empty;
}
