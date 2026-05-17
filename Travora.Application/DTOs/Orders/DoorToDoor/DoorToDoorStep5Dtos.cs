using Microsoft.AspNetCore.Http;
using Travora.Application.DTOs.External.Airline;

namespace Travora.Application.DTOs.Orders.DoorToDoor;

public class SetCustomsTypeRequest
{
    public string CustomsType { get; set; } = string.Empty; // "green_field" or "red_field"
}

public class SetCustomsTypeResponse
{
    public bool Success { get; set; }
    public string CustomsType { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CustomsLookupResponse
{
    public bool Found { get; set; }
    public string? ProductName { get; set; }
    public decimal? CustomsRatePercentage { get; set; }
    public string? Category { get; set; }
    public string? Message { get; set; }
}

public class AddCustomsItemRequest
{
    public string ExternalCategoryId { get; set; } = string.Empty;
    public string ExternalCategoryName { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public decimal DeclaredValue { get; set; }
    public int Quantity { get; set; }
    public IFormFile? PurchaseInvoice { get; set; }
    public List<IFormFile> PurchaseInvoices { get; set; } = new();
}

public class CustomsCategoryDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SubCategoriesCount { get; set; }
}

public class AddCustomsItemResponse
{
    public bool Success { get; set; }
    public DraftCustomsItem? AddedItem { get; set; }
    public decimal TotalDeclaredValue { get; set; }
    public decimal TotalCustomsFee { get; set; }
    public string? ErrorMessage { get; set; }
}
