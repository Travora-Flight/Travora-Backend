namespace Travora.Application.DTOs.Admin.Dashboard;

public class DashboardStatsResponse
{
    public int AllEmployees { get; set; }
    public int NewRequests { get; set; }
    public int CurrentRequests { get; set; }
    public int DoneRequests { get; set; }
    public List<WeeklyActivityItem> WeeklyActivity { get; set; } = new();
}
