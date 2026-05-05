namespace Travora.Domain.Enums;

public enum VerificationStatus
{
    Pending = 1,                // Pending
    UnderReview = 2,            // Under review
    Approved = 3,               // Approved
    Rejected = 4,               // Rejected
    Expired = 5,                // Expired
    ResubmissionRequired = 6    // Resubmission required
}
