using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Customer.Profile;

public class AddPaymentMethodRequest
{
    public string CardHolderName { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    
    [RegularExpression(@"^\d{3,4}$")]
    public string Cvv { get; set; } = string.Empty;
    
    public string PaymentFunding { get; set; } = string.Empty;
}
