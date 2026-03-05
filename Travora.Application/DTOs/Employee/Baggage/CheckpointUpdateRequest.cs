namespace Travora.Application.DTOs.Employee.Baggage;

public class CheckpointUpdateRequest
{
    public string BaggageTagNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
