namespace Travora.Application.DTOs.Admin.Passport;

public class PassportVerificationCountsResponse
{
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
}
