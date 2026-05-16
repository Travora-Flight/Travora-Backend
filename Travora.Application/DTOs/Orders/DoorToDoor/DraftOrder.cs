using Travora.Application.DTOs.External.Airline;

namespace Travora.Application.DTOs.Orders.DoorToDoor;

public class DraftOrder
{
    public string CustomerId { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public AirlineFlightInfo? FlightInfo { get; set; }
    public AirlinePassengerInfo? PassengerInfo { get; set; }
    public int BaggageCount { get; set; }
    public DateTime BookingDeadlineUtc { get; set; }
    public List<DraftCompanion> Companions { get; set; } = new();
    
    // Step 2.5 Validation
    public int TotalBaggageCount { get; set; }
    public bool BaggageValidated { get; set; }

    // Step 4 Pickup Slots
    public string? SelectedSlot { get; set; }
    public DateTime? SelectedSlotDate { get; set; }

    // Step 4.5 Delivery Slots
    public string? SelectedDeliverySlot { get; set; }
    public DateTime? SelectedDeliverySlotDate { get; set; }

    // Step 3 Location — Pickup
    public double? PickupLatitude { get; set; }
    public double? PickupLongitude { get; set; }
    public string? PickupFormattedAddress { get; set; }
    public string? PickupStreetAddress { get; set; }
    public string? PickupCity { get; set; }
    public string? PickupState { get; set; }
    public string? PickupCountry { get; set; }
    public string? PickupPostalCode { get; set; }
    
    // Step 3 Location — Delivery
    public double? DeliveryLatitude { get; set; }
    public double? DeliveryLongitude { get; set; }
    public string? DeliveryFormattedAddress { get; set; }
    public string? DeliveryStreetAddress { get; set; }
    public string? DeliveryCity { get; set; }
    public string? DeliveryState { get; set; }
    public string? DeliveryCountry { get; set; }
    public string? DeliveryPostalCode { get; set; }

    // Step 5 Customs
    public string? CustomsType { get; set; } // GreenField or RedField
    public List<DraftCustomsItem> CustomsItems { get; set; } = new();
    
    // Computed internally inside DraftOrder
    public decimal TotalCustomsFee => CustomsItems.Sum(x => x.TotalCustomsValue);
}

public class DraftCompanion
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public string SeatNumber { get; set; } = string.Empty;
    public string PassportImageUrl { get; set; } = string.Empty;
    public string? Nationality { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime? PassportExpiryDate { get; set; }
    public int BaggageCount { get; set; }
    public bool IsVerified { get; set; }
    public int PassportFileSizeKb { get; set; }
    public string PassportMimeType { get; set; } = string.Empty;
    public string? PassportOcrResultJson { get; set; }
}

public class DraftCustomsItem
{
    public string ItemType { get; set; } = "Other";
    public string ItemDescription { get; set; } = string.Empty;
    public decimal DeclaredValue { get; set; }
    public int Quantity { get; set; }
    public decimal CustomsRatePercentage { get; set; }
    public string PurchaseInvoiceUrl { get; set; } = string.Empty;
    public string ExternalCategoryId { get; set; } = string.Empty;
    public string ExternalCategoryName { get; set; } = string.Empty;

    public decimal TotalValue => DeclaredValue * Quantity;
    public decimal TotalCustomsValue => TotalValue * CustomsRatePercentage;
}
