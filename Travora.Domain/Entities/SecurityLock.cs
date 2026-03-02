using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class SecurityLock : ISoftDelete
{
    public int LockId { get; set; }
    public string LockCode { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RemovedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;

    // Foreign keys
    public int AppliedByEmployeeId { get; set; }
    public int BaggageId { get; set; }

    // Navigation properties
    public Employee AppliedByEmployee { get; set; } = null!;
    public Baggage Baggage { get; set; } = null!;
}
