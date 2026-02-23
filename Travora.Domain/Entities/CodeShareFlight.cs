using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class CodeShareFlight : IHasTimestamps
{
    public int CodeShareId { get; set; }
    public string MarketingAirlineName { get; set; } = string.Empty;
    public string MarketingFlightNumber { get; set; } = string.Empty;
    public string MarketingIataNumber { get; set; } = string.Empty;
    public string MarketingIcaoNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int OperatingFlightId { get; set; }
    public int MarketingAirlineId { get; set; }

    // Navigation properties
    public Flight OperatingFlight { get; set; } = null!;
    public Airline MarketingAirline { get; set; } = null!;
}
