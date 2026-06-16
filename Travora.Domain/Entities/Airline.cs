using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class Airline : IHasTimestamps
{
    public int AirlineId { get; set; }
    public string NameAirline { get; set; } = string.Empty;
    public string CodeIataAirline { get; set; } = string.Empty;
    public string CodeIcaoAirline { get; set; } = string.Empty;
    public string NameCountry { get; set; } = string.Empty;
    public string CodeIso2Country { get; set; } = string.Empty;
    public string Callsign { get; set; } = string.Empty;
    public string? CodeHub { get; set; }
    public int Founding { get; set; }
    public int SizeAirline { get; set; }
    public decimal AgeFleet { get; set; }
    public string IataPrefixAccounting { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string StatusAirline { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Country Country { get; set; } = null!;
    public Airport? HubAirport { get; set; }
    public ICollection<Aircraft> Aircrafts { get; set; } = new List<Aircraft>();
    public ICollection<Flight> Flights { get; set; } = new List<Flight>();
}
