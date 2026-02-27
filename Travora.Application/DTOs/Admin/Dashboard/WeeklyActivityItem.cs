namespace Travora.Application.DTOs.Admin.Dashboard;

public class WeeklyActivityItem
{
    public string Day { get; set; } = string.Empty;
    public int Completed { get; set; }
    public int NewRequests { get; set; }
    public int Ongoing { get; set; }
}
