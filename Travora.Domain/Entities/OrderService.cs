using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class OrderService : IHasTimestamps
{
    public int OrderServiceId { get; set; }
    public ServiceStatus ServiceStatus { get; set; } = ServiceStatus.Pending;
    public decimal ServiceFee { get; set; }
    public DateTime ScheduledStartTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int OrderId { get; set; }
    public int PackageServiceId { get; set; }
    public int? AssignedEmployeeId { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public PackageService PackageService { get; set; } = null!;
    public Employee? AssignedEmployee { get; set; }
    public ICollection<DriverTracking> DriverTrackings { get; set; } = new List<DriverTracking>();

    public bool CanEmployeeStart(DateTime now)
    {
        if (ServiceStatus != ServiceStatus.Assigned)
            return false;

        var phase = PackageService?.ExecutionPhase;
        if (phase == null)
            return false;

        return phase switch
        {
            ExecutionPhase.Pickup => ScheduledStartTime <= now.AddMinutes(30),
            ExecutionPhase.Delivery => ScheduledStartTime <= now.AddHours(4),
            ExecutionPhase.DepartureCheckin or ExecutionPhase.ArrivalCheckin => true,
            _ => false
        };
    }
}
