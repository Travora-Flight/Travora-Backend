namespace Travora.Application.DTOs.Admin.Employees;

public class EmployeePagedResponse
{
    public List<EmployeeListResponse> Employees { get; set; } = new();
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public int Total { get; set; }
}
