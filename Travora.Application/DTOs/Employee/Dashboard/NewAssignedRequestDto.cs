namespace Travora.Application.DTOs.Employee.Dashboard;

public class NewAssignedRequestDto
{
    public int OrderServiceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ScheduledTime { get; set; } = string.Empty;
    public string ScheduledDate { get; set; } = string.Empty;
    public bool CanStart { get; set; }
}
