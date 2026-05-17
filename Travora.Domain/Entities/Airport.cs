using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class Airport : IHasTimestamps
{
    public int AirportId { get; set; }
    public string NameAirport { get; set; } = string.Empty;
    public string CodeIataAirport { get; set; } = string.Empty;
    public string CodeIcaoAirport { get; set; } = string.Empty;
    public string? CodeIataCity { get; set; }
    public string CodeIso2Country { get; set; } = string.Empty;
    public decimal LatitudeAirport { get; set; }
    public decimal LongitudeAirport { get; set; }
    public string GMT { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string GeonameId { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public City? City { get; set; }
    public Country Country { get; set; } = null!;
    public ICollection<Flight> DepartureFlights { get; set; } = new List<Flight>();
    public ICollection<Flight> ArrivalFlights { get; set; } = new List<Flight>();
    public ICollection<WeatherSnapshot> WeatherSnapshots { get; set; } = new List<WeatherSnapshot>();
}
