namespace Travora.Application.DTOs.Employee.Tasks;

public class TaskDetailResponse
{
    public int OrderServiceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool CanStart { get; set; }
    public string ScheduledDate { get; set; } = string.Empty;
    public string ScheduledTime { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public decimal? GpsLatitude { get; set; }
    public decimal? GpsLongitude { get; set; }
    public string? MapUrl { get; set; }
    public ClientInfoDto? ClientInfo { get; set; }
    public int TotalBaggageCount { get; set; }
    public int ScannedCount { get; set; }
    public List<BaggageGroupDto> Bags { get; set; } = new();
}
