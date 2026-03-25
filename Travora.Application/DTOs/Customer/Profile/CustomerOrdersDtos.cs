namespace Travora.Application.DTOs.Customer.Profile;

public class CustomerOrderDto
{
    public int OrderId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string PackageType { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CustomerOrdersResponse
{
    public List<CustomerOrderDto> Orders { get; set; } = new();
    public string? Message { get; set; }
}
