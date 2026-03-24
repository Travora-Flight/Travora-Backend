 namespace Travora.Application.DTOs.Orders.DoorToDoor;

public class AvailableSlotsResponse
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public List<SlotItem> AvailableSlots { get; set; } = new();
    public string? CutoffTime { get; set; }
    public string? Note { get; set; }
}

public class SlotItem
{
    public string Slot { get; set; } = string.Empty;
    public bool Available { get; set; }
}

public class SelectSlotRequest
{
    public string Slot { get; set; } = string.Empty; // "10:00-12:00"
    public DateTime Date { get; set; }
}
