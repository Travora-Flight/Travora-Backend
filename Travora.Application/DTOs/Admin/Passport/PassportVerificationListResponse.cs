namespace Travora.Application.DTOs.Admin.Passport;

public class PassportVerificationListResponse
{
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public List<PassportVerificationItem> Passports { get; set; } = new();
}
