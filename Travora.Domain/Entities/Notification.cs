using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Notification
{
    public int NotificationId { get; set; }
    public int UserId { get; set; }
    public UserType UserType { get; set; }
    public NotificationType NotificationType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationChannel NotificationChannel { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;

    // Foreign keys
    public int? OrderId { get; set; }
    public int? BaggageId { get; set; }

    // Navigation properties
    public Order? Order { get; set; }
    public Baggage? Baggage { get; set; }
}
