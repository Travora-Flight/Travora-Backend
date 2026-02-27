namespace Travora.Application.DTOs.Admin.Passport;

public class PassportVerificationListResponse
{
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public List<PassportVerificationItem> Passports { get; set; } = new();
}

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

public class PassportInfoDetails
{
    public string PassportNumber { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public string IssueDate { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
}

public class RejectPassportRequest
{
    public string Reason { get; set; } = string.Empty;
}
