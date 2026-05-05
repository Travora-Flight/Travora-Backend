namespace Travora.Domain.Enums;

public enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5,
    rescheduled  = 6    // Order has been rescheduled
}
