namespace Travora.Application.DTOs.Flights.Tracker;

public class FlightDetailsResponse
{
    public string FlightIata { get; set; } = string.Empty;
    public string AirlineName { get; set; } = string.Empty;
    public string? AirlineLogoUrl { get; set; }
    public string From { get; set; } = string.Empty;
    public string FromCity { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public string UtcFrom { get; set; } = string.Empty;
    public string UtcTo { get; set; } = string.Empty;
    public AircraftInfo Aircraft { get; set; } = new();
    public decimal Speed { get; set; }
    public decimal Altitude { get; set; }
    public string? DepartureGate { get; set; }
    public string? DepartureTerminal { get; set; }
    public string? ArrivalGate { get; set; }
    public string? ArrivalTerminal { get; set; }
    public string ScheduledDeparture { get; set; } = string.Empty;
    public string ActualDeparture { get; set; } = string.Empty;
    public string ScheduledArrival { get; set; } = string.Empty;
    public string EstimatedArrival { get; set; } = string.Empty;
    public string? DelayMessage { get; set; }
    public string Status { get; set; } = string.Empty;
    public FlightPosition CurrentPosition { get; set; } = new();
    public List<FlightTrailPoint> FlightTrail { get; set; } = new();
}
