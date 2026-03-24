namespace Travora.Domain.Enums;

public enum InvoiceStatus
{
    Draft = 0,
    Pending = 1,
    Paid = 2,
    Overdue = 3,
    Cancelled = 4,
    Refunded = 5,
    Failed = 6
}
