using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class Country : IHasTimestamps
{
    public int CountryId { get; set; }
    public string CountryName { get; set; } = string.Empty;
    public string Iso2Code { get; set; } = string.Empty;
    public string Iso3Code { get; set; } = string.Empty;
    public string NumericIso { get; set; } = string.Empty;
    public string Continent { get; set; } = string.Empty;
    public string Capital { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public string CurrencyName { get; set; } = string.Empty;
    public string PhonePrefix { get; set; } = string.Empty;
    public long Population { get; set; }
    public string FipsCode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<City> Cities { get; set; } = new List<City>();
    public ICollection<Airline> Airlines { get; set; } = new List<Airline>();
    public ICollection<Airport> Airports { get; set; } = new List<Airport>();
}
