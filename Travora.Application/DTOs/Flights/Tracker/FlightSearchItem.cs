namespace Travora.Application.DTOs.Flights.Tracker;

public class FlightSearchItem
{
    public string FlightIata { get; set; } = string.Empty;
    public string AirlineIata { get; set; } = string.Empty;
    public string Registration { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Altitude { get; set; }
    public string DepartureIata { get; set; } = string.Empty;
    public string ArrivalIata { get; set; } = string.Empty;
}
