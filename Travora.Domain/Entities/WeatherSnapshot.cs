using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class WeatherSnapshot : IHasTimestamps
{
    public int WeatherSnapshotId { get; set; }
    public DateTime SnapshotTimestamp { get; set; } = DateTime.UtcNow;
    public string IcaoId { get; set; } = string.Empty;
    public DateTime ReceiptTime { get; set; }
    public DateTime ReportTime { get; set; }
    
    // Core Weather
    public decimal Temperature { get; set; }
    public decimal FeelsLike { get; set; }
    public int WindDirection { get; set; }
    public decimal WindSpeed { get; set; }
    public string Visibility { get; set; } = string.Empty;
    public decimal Altimeter { get; set; } // Map to pressure_mb
    public int Humidity { get; set; }
    
    // Condition
    public string ConditionText { get; set; } = string.Empty;
    public string ConditionIcon { get; set; } = string.Empty;
    public int ConditionCode { get; set; }
    
    // Astro & Forecast (Today)
    public string Sunrise { get; set; } = string.Empty;
    public string Sunset { get; set; } = string.Empty;
    public int ChanceOfRain { get; set; }
    public decimal MaxTemp { get; set; }
    public decimal MinTemp { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Airport Airport { get; set; } = null!;
}
