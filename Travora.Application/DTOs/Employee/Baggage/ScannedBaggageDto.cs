namespace Travora.Application.DTOs.Employee.Baggage;

public class ScannedBaggageDto
{
    public int BaggageId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public string? Destination { get; set; }
    public string? FlightNumber { get; set; }
    public string? Gate { get; set; }
    public string? Terminal { get; set; }
    public string? PassengerName { get; set; }
    public DateTime? DepartureTime { get; set; }
    public DateTime? BoardingTime { get; set; }
    public bool IsScanned { get; set; }
    public DateTime? ScannedAt { get; set; }
}
