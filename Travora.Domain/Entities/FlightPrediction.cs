using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class FlightPrediction : IHasTimestamps
{
    public int PredictionId { get; set; }
    public int PredictedDelayMinutes { get; set; }
    public decimal PredictionConfidenceScore { get; set; }
    public DateTime PredictionTimestamp { get; set; } = DateTime.UtcNow;
    public string PredictionModelVersion { get; set; } = string.Empty;
    public int? ActualDelayMinutes { get; set; }
    public decimal? PredictionAccuracy { get; set; }
    public string? FactorsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int FlightId { get; set; }
    public int WeatherSnapshotId { get; set; }

    // Navigation properties
    public Flight Flight { get; set; } = null!;
    public WeatherSnapshot WeatherSnapshot { get; set; } = null!;
}
