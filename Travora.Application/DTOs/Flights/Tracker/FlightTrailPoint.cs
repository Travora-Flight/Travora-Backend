namespace Travora.Application.DTOs.Flights.Tracker;

public class FlightTrailPoint
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal Altitude { get; set; }
    public decimal Speed { get; set; }
    public decimal Heading { get; set; }
    public bool IsOnGround { get; set; }
    public long Timestamp { get; set; }
}
