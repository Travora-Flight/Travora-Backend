namespace Travora.Domain.Entities;

public class Companion
{
    public int CompanionId { get; set; }
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }
    public string PassportNumber { get; set; } = string.Empty;
    public string? Nationality { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime? PassportExpiryDate { get; set; }
    public bool IsVerified { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<OrderCompanion> OrderCompanions { get; set; } = new List<OrderCompanion>();
    public ICollection<Baggage> Baggages { get; set; } = new List<Baggage>();
    public ICollection<BoardingPass> BoardingPasses { get; set; } = new List<BoardingPass>();
}
