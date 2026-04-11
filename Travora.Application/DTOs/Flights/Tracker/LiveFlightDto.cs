namespace Travora.Application.DTOs.Flights.Tracker;

public class LiveFlightDto
{
    public string FlightIata { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal Altitude { get; set; }
    public decimal Heading { get; set; }
    public decimal Speed { get; set; }
    public bool IsOnGround { get; set; }
    public string Status { get; set; } = string.Empty;
    public string AirlineIata { get; set; } = string.Empty;
    public string Registration { get; set; } = string.Empty;
    public string DepartureIata { get; set; } = string.Empty;
    public string ArrivalIata { get; set; } = string.Empty;
    public string? ScheduledDeparture { get; set; }
    public string? ScheduledArrival { get; set; }
}
