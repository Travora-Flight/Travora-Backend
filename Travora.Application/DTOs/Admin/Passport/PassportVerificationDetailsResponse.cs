using System;

namespace Travora.Application.DTOs.Admin.Passport;

public class PassportVerificationDetailsResponse
{
    public int DocumentId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestDate { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PassportImageUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public decimal OcrConfidenceScore { get; set; }
    public bool ManualReviewRequired { get; set; }
    
    // Original Passport Info entered by user
    public PassportInfoDetails PassportInfo { get; set; } = new();
    public string ReviewReason { get; set; } = string.Empty;
}
