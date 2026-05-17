using System;
using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class CustomsItemInvoice : IHasTimestamps
{
    public int CustomsItemInvoiceId { get; set; }
    public int CustomsItemId { get; set; }
    public string InvoicePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public CustomsItem CustomsItem { get; set; } = null!;
}
