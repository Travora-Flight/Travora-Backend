namespace Travora.Domain.Enums;

public enum RefundStatus
{
    Requested,
    PendingApproval,
    Approved,
    Processing,
    Completed,
    Rejected,
    Cancelled
}
