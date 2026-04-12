namespace Travora.Application.DTOs.Orders;

public class OrderListDto
{
    public int OrderId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
}
