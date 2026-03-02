namespace Travora.Application.DTOs.Admin.Passport;

public class PassportVerificationItem
{
    public int DocumentId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestDate { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PassportImageUrl { get; set; } = string.Empty;
    public PassportInfoDetails PassportInfo { get; set; } = new();
    public string Address { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal OcrConfidenceScore { get; set; }
    public bool ManualReviewRequired { get; set; }
}
