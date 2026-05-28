namespace Travora.Application.DTOs.Employee.Tasks;

/// <summary>
/// Customs declaration summary shown to the ArrivalBaggageHandling employee.
/// </summary>
public class CustomsInfoDto
{
    public string DeclarationType { get; set; } = string.Empty;  // GreenField / RedField
    public decimal TotalDeclaredValue { get; set; }
    public decimal TotalCustomsFee { get; set; }
    public string? Notes { get; set; }
    public List<CustomsItemDto> Items { get; set; } = new();
}

public class CustomsItemDto
{
    public string ItemDescription { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal DeclaredValue { get; set; }
    public decimal TotalValue { get; set; }
    public decimal CustomsRatePercentage { get; set; }
    public decimal CustomsFee { get; set; }
    public List<string> InvoiceUrls { get; set; } = new();
}
