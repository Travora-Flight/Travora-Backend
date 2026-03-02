namespace Travora.Application.DTOs.Admin.Requests;

public class RequestDetailResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public ClientInfo ClientInfo { get; set; } = new();
    public ServiceDetails ServiceDetails { get; set; } = new();
    public List<TimelineItem> Timeline { get; set; } = new();
}
