namespace Travora.Application.DTOs.Admin.Requests;

public class TimelineItem
{
    public string Event { get; set; } = string.Empty;
    public string? Time { get; set; }
    public bool IsDone { get; set; }
}
