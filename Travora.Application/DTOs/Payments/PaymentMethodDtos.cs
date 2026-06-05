namespace Travora.Application.DTOs.Payments;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string CardHolderName { get; set; } = string.Empty;
    public string CardLastFour { get; set; } = string.Empty;
    public string CardBrand { get; set; } = string.Empty;
    public string PaymentFunding { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class PaymentMethodsResponse
{
    public decimal Balance { get; set; } = 0.00m;
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
