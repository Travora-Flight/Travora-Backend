namespace Travora.Application.DTOs.Employee.Baggage;

public class BaggageScanRequest
{
    public string QrData { get; set; } = string.Empty;
    public int BaggageId { get; set; }
    public int OrderServiceId { get; set; }
}
