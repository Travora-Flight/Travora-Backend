using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class WeatherSnapshot : IHasTimestamps
{
    public int WeatherSnapshotId { get; set; }
    public DateTime SnapshotTimestamp { get; set; } = DateTime.UtcNow;
    public string IcaoId { get; set; } = string.Empty;
    public DateTime ReceiptTime { get; set; }
    public DateTime ReportTime { get; set; }
    public decimal Temperature { get; set; }
    public decimal Dewpoint { get; set; }
    public int WindDirection { get; set; }
    public decimal WindSpeed { get; set; }
    public string Visibility { get; set; } = string.Empty;
    public decimal Altimeter { get; set; }
    public string MetarType { get; set; } = string.Empty;
    public string RawObservation { get; set; } = string.Empty;
    public int Elevation { get; set; }
    public string CloudCover { get; set; } = string.Empty;
    public FlightCategory FlightCategory { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Airport Airport { get; set; } = null!;
    public ICollection<CloudLayer> CloudLayers { get; set; } = new List<CloudLayer>();
    public ICollection<FlightPrediction> FlightPredictions { get; set; } = new List<FlightPrediction>();
}
