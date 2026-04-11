namespace Travora.Application.DTOs.Flights.Tracker;

public class AirportSearchItem
{
    public string IataCode { get; set; } = string.Empty;
    public string IcaoCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}
