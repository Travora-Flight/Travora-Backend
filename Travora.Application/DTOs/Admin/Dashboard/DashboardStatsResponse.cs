namespace Travora.Application.DTOs.Admin.Dashboard;

public class DashboardStatsResponse
{
    public int AllEmployees { get; set; }
    public int AllEmployeesGrowth { get; set; }
    
    public int NewRequests { get; set; }
    public int NewRequestsGrowth { get; set; }
    
    public int CurrentRequests { get; set; }
    public int CurrentRequestsChange { get; set; }
    
    public int DoneRequests { get; set; }
    public int DoneRequestsGrowth { get; set; }
    
    public List<WeeklyActivityItem> WeeklyActivity { get; set; } = new();
    public List<OnlineEmployeeItem> OnlineEmployees { get; set; } = new();
    public List<LastRequestItem> LastRequests { get; set; } = new();
}
