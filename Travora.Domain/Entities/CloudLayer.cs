namespace Travora.Domain.Entities;

public class CloudLayer
{
    public int CloudLayerId { get; set; }
    public string CoverType { get; set; } = string.Empty;
    public int BaseAltitudeFeet { get; set; }

    // Foreign keys
    public int WeatherSnapshotId { get; set; }

    // Navigation properties
    public WeatherSnapshot WeatherSnapshot { get; set; } = null!;
}
