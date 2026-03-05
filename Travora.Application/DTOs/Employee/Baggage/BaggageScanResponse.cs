namespace Travora.Application.DTOs.Employee.Baggage;

public class BaggageScanResponse
{
    public bool Success { get; set; }
    public ScannedBaggageDto? Baggage { get; set; }
    public BaggageOwnerDto? Owner { get; set; }
}
