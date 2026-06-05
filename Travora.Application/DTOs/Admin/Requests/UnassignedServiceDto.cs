using System;

namespace Travora.Application.DTOs.Admin.Requests;

public class UnassignedServiceDto
{
    public int OrderServiceId { get; set; }
    public int OrderId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ExecutionPhase { get; set; } = string.Empty;
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}
