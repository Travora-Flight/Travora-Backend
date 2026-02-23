namespace Travora.Domain.Common;

public abstract class AuditableEntity : BaseEntity, IHasTimestamps
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
