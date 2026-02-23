using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class Feedback : IHasTimestamps
{
    public int FeedbackId { get; set; }
    public int Rating { get; set; }
    public int ServiceQualityRating { get; set; }
    public int PunctualityRating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsPublished { get; set; } = false;

    // Foreign keys
    public int OrderId { get; set; }
    public int CustomerId { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
}
