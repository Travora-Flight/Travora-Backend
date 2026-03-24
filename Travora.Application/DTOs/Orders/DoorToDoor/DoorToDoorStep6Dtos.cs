namespace Travora.Application.DTOs.Orders.DoorToDoor;

public class InvoiceResponse
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public InvoiceBreakdown Breakdown { get; set; } = new();
}

public class InvoiceBreakdown
{
    public decimal PackageValue { get; set; }
    public BaggageDetails BaggageDetails { get; set; } = new();
    public CompanionDetailsInvoice CompanionDetails { get; set; } = new();
    public decimal CustomsValue { get; set; }
    public decimal CustomsFee { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class BaggageDetails
{
    public int IncludedBags { get; set; }
    public int TotalBags { get; set; }
    public int ExtraBags { get; set; }
    public decimal ExtraBaggageFee { get; set; }
}

public class CompanionDetailsInvoice
{
    public int IncludedCompanions { get; set; }
    public int TotalCompanions { get; set; }
    public int ExtraCompanions { get; set; }
    public decimal ExtraCompanionsFee { get; set; }
}

public class ConfirmOrderResponse
{
    public bool IsValid { get; set; }
    public bool Success { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal TotalPaid { get; set; }
    public string? ErrorMessage { get; set; }
}
