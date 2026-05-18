using Travora.Domain.Enums;

namespace Travora.Application.DTOs.Orders;

public class RescheduleRequest
{
    public RescheduleType Type { get; set; }
    public DateTime NewDate { get; set; }
    public string NewTimeSlot { get; set; } = string.Empty;
}

public class RescheduleResponse
{
    public bool Success { get; set; }
    public string? NewDate { get; set; }
    public string? NewTimeSlot { get; set; }
    public string Message { get; set; } = string.Empty;
}
