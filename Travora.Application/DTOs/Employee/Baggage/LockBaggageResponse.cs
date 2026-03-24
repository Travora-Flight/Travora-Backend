namespace Travora.Application.DTOs.Employee.Baggage;

public class LockBaggageResponse
{
    public bool Success { get; set; }
    public int BaggageId { get; set; }
    public string LockCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
