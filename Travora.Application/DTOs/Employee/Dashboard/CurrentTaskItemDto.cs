namespace Travora.Application.DTOs.Employee.Dashboard;

public class CurrentTaskItemDto
{
    public int OrderServiceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ScheduledTime { get; set; } = string.Empty;
}
