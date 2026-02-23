using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class City : IHasTimestamps
{
    public int CityId { get; set; }
    public string NameCity { get; set; } = string.Empty;
    public string CodeIataCity { get; set; } = string.Empty;
    public string CodeIso2Country { get; set; } = string.Empty;
    public decimal LatitudeCity { get; set; }
    public decimal LongitudeCity { get; set; }
    public string Timezone { get; set; } = string.Empty;
    public string GMT { get; set; } = string.Empty;
    public int? GeonameId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Country Country { get; set; } = null!;
    public ICollection<Airport> Airports { get; set; } = new List<Airport>();
}
