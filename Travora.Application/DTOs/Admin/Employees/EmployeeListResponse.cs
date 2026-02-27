namespace Travora.Application.DTOs.Admin.Employees;

public class EmployeeListResponse
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ShiftType { get; set; } = string.Empty;
    public string JobRole { get; set; } = string.Empty;
}

public class EmployeePagedResponse
{
    public List<EmployeeListResponse> Employees { get; set; } = new();
    public int Total { get; set; }
}
