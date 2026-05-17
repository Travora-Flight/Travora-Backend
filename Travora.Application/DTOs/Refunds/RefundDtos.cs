namespace Travora.Application.DTOs.Refunds;

public class RefundRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class RefundResponse
{
    public bool Success { get; set; }
    public int RefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class RefundStatusResponse
{
    public int RefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
}

public class AdminRefundListItem
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public decimal OrderAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
}

public class AdminRefundDetail
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public decimal OrderAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessedByAdmin { get; set; }
}

public class AdminProcessRefundRequest
{
    public string? Notes { get; set; }
}
