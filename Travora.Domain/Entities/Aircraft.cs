using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class Aircraft : IHasTimestamps
{
    public int AirplaneId { get; set; }
    public string NumberRegistration { get; set; } = string.Empty;
    public string HexIcaoAirplane { get; set; } = string.Empty;
    public string AirplaneIataType { get; set; } = string.Empty;
    public string CodeIataPlaneLong { get; set; } = string.Empty;
    public string CodeIataPlaneShort { get; set; } = string.Empty;
    public string CodeIataAirline { get; set; } = string.Empty;
    public string CodeIcaoAirline { get; set; } = string.Empty;
    public string ConstructionNumber { get; set; } = string.Empty;
    public DateTime? DeliveryDate { get; set; }
    public DateTime? FirstFlight { get; set; }
    public string LineNumber { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public int EnginesCount { get; set; }
    public string EnginesType { get; set; } = string.Empty;
    public int PlaneAge { get; set; }
    public string? PlaneClass { get; set; }
    public string PlaneModel { get; set; } = string.Empty;
    public string PlaneSeries { get; set; } = string.Empty;
    public string PlaneOwner { get; set; } = string.Empty;
    public string PlaneStatus { get; set; } = string.Empty;
    public string ProductionLine { get; set; } = string.Empty;
    public DateTime? RegistrationDate { get; set; }
    public DateTime? RolloutDate { get; set; }
    public string? NumberTestRegistration { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int? AirlineId { get; set; }

    // Navigation properties
    public Airline? Airline { get; set; }
    public ICollection<Flight> Flights { get; set; } = new List<Flight>();
}
