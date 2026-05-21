namespace Travora.Application.DTOs.Orders;

public class OrderDetailsResponse
{
    public int OrderId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? From { get; set; }
    public string? To { get; set; }
    public int NumberOfBags { get; set; }
    public decimal TotalWeight { get; set; }
    public int NumberOfPassengers { get; set; }
    public bool CanCancel { get; set; }
    public bool HasBoardingPass { get; set; }
    public AppointmentDto? Appointment { get; set; }
    public List<TrackingStepDto> TrackingStatus { get; set; } = new();
    public string? TrackingMessage { get; set; }
}

public class AppointmentDto
{
    public AppointmentSlot? Pickup { get; set; }
    public AppointmentSlot? Delivery { get; set; }
}

public class AppointmentSlot
{
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
}

public class TrackingStepDto
{
    public string Step { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? Timestamp { get; set; }
    public bool IsDone { get; set; }
}
