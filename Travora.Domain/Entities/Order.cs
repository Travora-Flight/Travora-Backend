using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Order : IHasTimestamps
{
    public int OrderId { get; set; }
    public OrderStatus OrderStatus { get; set; } = OrderStatus.Pending;

    // Companions Pricing
    public int ExtraCompanionsCount { get; set; } = 0;
    public decimal ExtraCompanionsFee { get; set; } = 0;

    // Baggage Pricing
    public int TotalBaggageCount { get; set; }
    public int ExtraBaggageCount { get; set; } = 0;
    public decimal ExtraBaggageFee { get; set; } = 0;

    public decimal TotalAmount { get; set; }
    public string? SpecialInstructions { get; set; }
    public string? CancellationReason { get; set; }

    public DateTime PickupDate { get; set; }
    public string PickupTimeSlot { get; set; } = string.Empty;
    public DateTime DeliveryDate { get; set; }
    public string DeliveryTimeSlot { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int CustomerId { get; set; }
    public int FlightId { get; set; }
    public int PackageId { get; set; }
    public int PickupLocationId { get; set; }
    public int DeliveryLocationId { get; set; }

    // Navigation properties
    public Customer Customer { get; set; } = null!;
    public Flight Flight { get; set; } = null!;
    public Package Package { get; set; } = null!;
    public Location PickupLocation { get; set; } = null!;
    public Location DeliveryLocation { get; set; } = null!;
    public ICollection<OrderCompanion> OrderCompanions { get; set; } = new List<OrderCompanion>();
    public ICollection<OrderService> OrderServices { get; set; } = new List<OrderService>();
    public ICollection<Baggage> Baggages { get; set; } = new List<Baggage>();
    public ICollection<BoardingPass> BoardingPasses { get; set; } = new List<BoardingPass>();
    public ICollection<CustomsDeclaration> CustomsDeclarations { get; set; } = new List<CustomsDeclaration>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
}
