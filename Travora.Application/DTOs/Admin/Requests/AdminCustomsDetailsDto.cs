using System.Collections.Generic;

namespace Travora.Application.DTOs.Admin.Requests;

public class AdminCustomsDetailsDto
{
    public bool HasCustoms { get; set; }
    public string CustomsMessage { get; set; } = string.Empty;
    public string CustomsType { get; set; } = string.Empty; // GreenField / RedField
    public decimal TotalDeclaredValue { get; set; }
    public decimal TotalCustomsFee { get; set; }
    public string? Notes { get; set; }
    public List<AdminCustomsItemDto> Items { get; set; } = new();
}

public class AdminCustomsItemDto
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
