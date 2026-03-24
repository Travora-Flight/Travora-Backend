namespace Travora.Application.DTOs.Orders;

public class CancelOrderRequest
{
    public string CancellationReason { get; set; } = string.Empty;
}

public class CancelOrderResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
