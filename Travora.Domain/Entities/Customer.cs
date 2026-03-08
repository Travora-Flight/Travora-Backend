using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Customer : IHasTimestamps, ISoftDelete
{
    public int CustomerId { get; set; }
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public DateTime PassportExpiryDate { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string Nationality { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public CustomerAccountStatus AccountStatus { get; set; } = CustomerAccountStatus.PendingVerification;
    public bool EmailVerified { get; set; } = false;
    public bool ProfileCompleted { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLogin { get; set; }

    // Navigation properties
    public ICollection<CustomerCompanion> CustomerCompanions { get; set; } = new List<CustomerCompanion>();
    public ICollection<Location> Locations { get; set; } = new List<Location>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<SavedFlight> SavedFlights { get; set; } = new List<SavedFlight>();
    public ICollection<Baggage> Baggages { get; set; } = new List<Baggage>();
    public ICollection<BoardingPass> BoardingPasses { get; set; } = new List<BoardingPass>();
    public ICollection<PaymentMethod> PaymentMethods { get; set; } = new List<PaymentMethod>();
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
    public ICollection<LoginLog> LoginLogs { get; set; } = new List<LoginLog>();
    public ICollection<QrScan> QrScans { get; set; } = new List<QrScan>();
    public ICollection<BaggagePhoto> BaggagePhotos { get; set; } = new List<BaggagePhoto>();
}
