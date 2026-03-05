namespace Travora.Application.DTOs.Employee.Dashboard;

public class EmployeeDashboardResponse
{
    public string Greeting { get; set; } = string.Empty;
    public EmployeeStatsDto Stats { get; set; } = new();
    public List<CurrentTaskItemDto> CurrentTasks { get; set; } = new();
    public List<NewAssignedRequestDto> NewAssignedRequests { get; set; } = new();
}
