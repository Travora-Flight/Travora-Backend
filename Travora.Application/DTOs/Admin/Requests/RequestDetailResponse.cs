namespace Travora.Application.DTOs.Admin.Requests;

public class RequestDetailResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public ClientInfo ClientInfo { get; set; } = new();
    public ServiceDetails ServiceDetails { get; set; } = new();
    public List<TimelineItem> Timeline { get; set; } = new();
}

public class ClientInfo
{
    public string Name { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string MapUrl { get; set; } = string.Empty;
}

public class ServiceDetails
{
    public string ServiceType { get; set; } = string.Empty;
    public string AssignedEmployee { get; set; } = string.Empty;
}

public class TimelineItem
{
    public string Event { get; set; } = string.Empty;
    public string? Time { get; set; }
    public bool IsDone { get; set; }
}
