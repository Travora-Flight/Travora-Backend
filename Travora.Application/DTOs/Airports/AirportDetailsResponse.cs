 namespace Travora.Application.DTOs.Airports;

public class AirportDetailsResponse
{
    public string AirportName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string IataCode { get; set; } = string.Empty;
    public string IcaoCode { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public WeatherDto? Weather { get; set; }
    public int TotalFlights { get; set; }
    public List<AirportFlightDto> Flights { get; set; } = new();
}
