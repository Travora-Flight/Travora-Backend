namespace Travora.API.Configurations;

public class PassportOcrSettings
{
    public double ConfidenceThreshold { get; set; } = 0.85;
    public double ManualReviewThreshold { get; set; } = 0.60;
}
