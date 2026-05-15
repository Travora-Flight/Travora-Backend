namespace Travora.Application.DTOs.Employee.Tasks;

public class CompletedTasksResponse
{
    public int TotalCompleted { get; set; }
    public List<CompletedTaskItemDto> Tasks { get; set; } = new();
}

public class CompletedTaskItemDto
{
    public int OrderServiceId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ScheduledDate { get; set; } = string.Empty;
    public string ScheduledTime { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
    public int BaggageCount { get; set; }
}
