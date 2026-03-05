namespace Travora.Application.DTOs.Employee.Tasks;

public class TaskActionResponse
{
    public bool Success { get; set; }
    public int OrderServiceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool? OrderCompleted { get; set; }
}
