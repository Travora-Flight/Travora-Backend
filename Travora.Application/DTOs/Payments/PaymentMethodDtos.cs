namespace Travora.Application.DTOs.Payments;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string CardLastFour { get; set; } = string.Empty;
    public string CardBrand { get; set; } = string.Empty;
    public int CardExpiryMonth { get; set; }
    public int CardExpiryYear { get; set; }
    public string PaymentFunding { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class PaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
