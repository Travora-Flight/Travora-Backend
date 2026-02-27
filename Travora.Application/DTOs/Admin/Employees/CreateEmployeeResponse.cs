namespace Travora.Application.DTOs.Admin.Employees;

public class CreateEmployeeResponse
{
    public bool Success { get; set; }
    public int EmployeeId { get; set; }
    public string GeneratedEmail { get; set; } = string.Empty;
    public string TempPassword { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
