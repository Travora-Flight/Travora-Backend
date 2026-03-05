namespace Travora.Application.DTOs.Employee.Location;

public class DriverLocationRequest
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal? AccuracyMeters { get; set; }
    public decimal? SpeedKmh { get; set; }
    public decimal? HeadingDegrees { get; set; }
    public DateTime TrackedAtUtc { get; set; }
    public bool IsMoving { get; set; }
    public int? OrderServiceId { get; set; }
}
