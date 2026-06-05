using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Employee.Tasks;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Employee;
using Travora.Domain.Constants;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.EmployeePanel.Services;

public class EmployeeTaskService : IEmployeeTaskService
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationPusher _pusher;
    private readonly IRefundService _refundService;

    // Predefined cancellation reasons for the customs employee
    private static readonly List<CancelReasonDto> CancelReasons = new()
    {
        new CancelReasonDto
        {
            Id = 1,
            Title = "Incorrect customs declaration",
            Description = "Customer declared fewer items than the actual count"
        },
        new CancelReasonDto
        {
            Id = 2,
            Title = "Undeclared customs items",
            Description = "Customer has customs items but did not declare them"
        }
    };

    public EmployeeTaskService(ApplicationDbContext db, INotificationPusher pusher, IRefundService refundService)
    {
        _db = db;
        _pusher = pusher;
        _refundService = refundService;
    }

    public async Task<TaskDetailResponse> GetTaskDetailAsync(int employeeId, int orderServiceId)
    {
        var os = await _db.OrderServices
            .Include(x => x.Order).ThenInclude(o => o.Customer)
            .Include(x => x.Order).ThenInclude(o => o.PickupLocation)
            .Include(x => x.Order).ThenInclude(o => o.Package)
            .Include(x => x.Order).ThenInclude(o => o.Baggages).ThenInclude(b => b.BaggagePhotos)
            .Include(x => x.Order).ThenInclude(o => o.Baggages).ThenInclude(b => b.BaggageTrackings)
            .Include(x => x.Order).ThenInclude(o => o.Baggages).ThenInclude(b => b.Customer)
            .Include(x => x.Order).ThenInclude(o => o.Baggages).ThenInclude(b => b.Companion)
            .Include(x => x.Order).ThenInclude(o => o.CustomsDeclarations).ThenInclude(cd => cd.CustomsItems).ThenInclude(ci => ci.Invoices)
            .Include(x => x.PackageService).ThenInclude(ps => ps.Service)
            .FirstOrDefaultAsync(x => x.OrderServiceId == orderServiceId)
            ?? throw new KeyNotFoundException("Task not found");

        if (os.AssignedEmployeeId != employeeId)
            throw new UnauthorizedAccessException("Unauthorized");

        var now = DateTime.UtcNow;
        var canStart = os.ServiceStatus == ServiceStatus.Assigned
            && os.ScheduledStartTime <= now.AddMinutes(30);
       
        var order = os.Order;
        var location = order.PickupLocation;

        // Query scanned baggage IDs for THIS order service only (not globally)
        var orderBaggageIds = order.Baggages.Select(b => b.BaggageId).ToList();
        var scannedBaggageIds = await _db.QrScans
            .Where(q => orderBaggageIds.Contains(q.BaggageId) && q.OrderServiceId == orderServiceId)
            .Select(q => q.BaggageId)
            .Distinct()
            .ToListAsync();

        // Query photo counts for THIS order service only (each employee sees their own photos)
        var photoCountsByBaggage = await _db.BaggagePhotos
            .Where(p => orderBaggageIds.Contains(p.BaggageId) && p.OrderServiceId == orderServiceId)
            .GroupBy(p => p.BaggageId)
            .Select(g => new { BaggageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BaggageId, x => x.Count);

        var groupedBags = order.Baggages.GroupBy(b => new { b.OwnerType, b.CompanionId, b.CustomerId })
            .Select(g =>
            {
                var first = g.First();
                string ownerName = "";
                if (first.OwnerType == Domain.Enums.BaggageOwnerType.Customer && first.Customer != null)
                    ownerName = $"{first.Customer.Firstname} {first.Customer.Lastname}";
                else if (first.OwnerType == Domain.Enums.BaggageOwnerType.Companion && first.Companion != null)
                    ownerName = $"{first.Companion.Firstname} {first.Companion.Lastname}";

                return new BaggageGroupDto
                {
                    OwnerType = first.OwnerType.ToString().ToLower(),
                    OwnerName = ownerName,
                    BaggageCount = g.Count(),
                    Bags = g.Select(b =>
                    {
                        var lastTracking = b.BaggageTrackings
                            .OrderByDescending(t => t.ArrivalTime)
                            .FirstOrDefault();

                        return new TaskBagItemDto
                        {
                            BaggageId = b.BaggageId,
                            TagNumber = b.BaggageNumber,
                            WeightKg = b.TotalWeight,
                            Destination = b.Destination,
                            CurrentStatus = lastTracking?.Status.ToString(),
                            IsScanned = scannedBaggageIds.Contains(b.BaggageId),
                            PhotosCount = photoCountsByBaggage.GetValueOrDefault(b.BaggageId, 0)
                        };
                    }).ToList()
                };
            }).ToList();

        var customer = order.Customer;
        var isArrivalHandling = os.PackageService?.Service?.ServiceType == ServiceType.ArrivalBaggageHandling;

        // Build customs data only for ArrivalBaggageHandling
        CustomsInfoDto? customsInfo = null;

        if (isArrivalHandling)
        {
            var declaration = order.CustomsDeclarations.FirstOrDefault();
            if (declaration != null)
            {
                customsInfo = new CustomsInfoDto
                {
                    DeclarationType = declaration.CustomsType.ToString(),
                    TotalDeclaredValue = declaration.TotalDeclaredValue,
                    TotalCustomsFee = declaration.TotalCustomsFee,
                    Notes = declaration.Notes,
                    Items = declaration.CustomsItems.Select(ci => new CustomsItemDto
                    {
                        ItemDescription = ci.ItemDescription,
                        Category = ci.ExternalCategoryName,
                        Quantity = ci.Quantity,
                        DeclaredValue = ci.DeclaredValue,
                        TotalValue = ci.TotalValue,
                        CustomsRatePercentage = ci.CustomsRatePercentage,
                        CustomsFee = ci.TotalCustomsValue,
                        InvoiceUrls = ci.Invoices.Select(inv => inv.InvoicePath).ToList()
                    }).ToList()
                };
            }
        }

        return new TaskDetailResponse
        {
            OrderServiceId = os.OrderServiceId,
            Status = os.ServiceStatus.ToString(),
            CanStart = canStart,
            ScheduledDate = os.ScheduledStartTime.ToString("dd/MM"),
            ScheduledTime = os.ScheduledStartTime.ToString("hh:mm tt"),
            Type = os.PackageService?.Service?.ServiceName ?? string.Empty,
            Location = $"{location.City}, {location.StreetAddress}",
            GpsLatitude = location.GpsLatitude,
            GpsLongitude = location.GpsLongitude,
            MapUrl = $"https://maps.google.com/?q={location.GpsLatitude},{location.GpsLongitude}",
            ClientInfo = new ClientInfoDto
            {
                Name = $"{customer.Firstname} {customer.Lastname}",
                Mobile = customer.PhoneNumber,
                PassportNumber = isArrivalHandling ? customer.PassportNumber : null,
                Nationality = isArrivalHandling ? customer.Nationality : null,
                PassportExpiryDate = isArrivalHandling ? customer.PassportExpiryDate.ToString("dd/MM/yyyy") : null
            },
            TotalBaggageCount = order.TotalBaggageCount,
            ScannedCount = scannedBaggageIds.Count,
            Bags = groupedBags,
            CustomsInfo = customsInfo
        };
    }

    public async Task<TaskActionResponse> StartTaskAsync(int employeeId, int orderServiceId)
    {
        var os = await _db.OrderServices
            .Include(x => x.PackageService)
            .Include(x => x.Order).ThenInclude(o => o.OrderServices)
                .ThenInclude(s => s.PackageService)
            .Include(x => x.Order).ThenInclude(o => o.Package)
            .FirstOrDefaultAsync(x => x.OrderServiceId == orderServiceId)
            ?? throw new KeyNotFoundException("Task not found");

        if (os.AssignedEmployeeId != employeeId)
            throw new UnauthorizedAccessException("Unauthorized");

        if (os.ServiceStatus != ServiceStatus.Assigned)
            throw new InvalidOperationException("Task not assigned yet or already started");

        var now = DateTime.UtcNow;
        var currentPhase = os.PackageService?.ExecutionPhase;

        // Phase-aware CanStart validation
        switch (currentPhase)
        {
            case ExecutionPhase.Pickup:
                if (os.ScheduledStartTime > now.AddMinutes(30))
                    throw new InvalidOperationException("Cannot start yet. You can begin 30 minutes before the scheduled time.");
                break;
            case ExecutionPhase.Delivery:
                if (os.ScheduledStartTime > now.AddHours(4))
                    throw new InvalidOperationException("Cannot start yet. You can begin 4 hours before the scheduled delivery time.");
                break;
            // DepartureCheckin & ArrivalCheckin: can start immediately after assignment
        }

        os.ServiceStatus = ServiceStatus.InProgress;
        os.ActualStartTime = now;
        os.UpdatedAt = now;

        // Set ActualEndTime on the PREVIOUS phase (the previous employee's job ends when the next one starts)
        var previousPhase = currentPhase switch
        {
            ExecutionPhase.DepartureCheckin => ExecutionPhase.Pickup,
            ExecutionPhase.ArrivalCheckin => ExecutionPhase.DepartureCheckin,
            ExecutionPhase.Delivery => ExecutionPhase.ArrivalCheckin,
            _ => (ExecutionPhase?)null
        };

        if (previousPhase.HasValue)
        {
            var prevService = os.Order.OrderServices
                .FirstOrDefault(s => s.PackageService?.ExecutionPhase == previousPhase.Value
                                  && s.ServiceStatus == ServiceStatus.Completed
                                  && s.ActualEndTime == null);
            if (prevService != null)
            {
                prevService.ActualEndTime = now;
                prevService.UpdatedAt = now;
            }
        }

        // Update order status if this is the first service to start
        var order = os.Order;
        if (order.OrderStatus == OrderStatus.Confirmed)
        {
            order.OrderStatus = OrderStatus.InProgress;
            order.UpdatedAt = now;
        }

        // Update tracking status based on phase
        if (currentPhase == ExecutionPhase.DepartureCheckin)
        {
            // ArrivedAtAirport — bags arrived at departure airport
            foreach (var bag in await _db.Baggages.Where(b => b.OrderId == order.OrderId).ToListAsync())
            {
                _db.BaggageTrackings.Add(new BaggageTracking
                {
                    Status = BaggageTrackingStatus.ArrivedAtAirport,
                    HandledByEmployeeId = employeeId,
                    BaggageId = bag.BaggageId,
                    ArrivalTime = now,
                    GpsLatitude = 0,
                    GpsLongitude = 0
                });
            }
        }
        else if (currentPhase == ExecutionPhase.Delivery)
        {
            // OutForDelivery — driver is on the way
            foreach (var bag in await _db.Baggages.Where(b => b.OrderId == order.OrderId).ToListAsync())
            {
                _db.BaggageTrackings.Add(new BaggageTracking
                {
                    Status = BaggageTrackingStatus.OutForDelivery,
                    HandledByEmployeeId = employeeId,
                    BaggageId = bag.BaggageId,
                    ArrivalTime = now,
                    GpsLatitude = 0,
                    GpsLongitude = 0
                });
            }
        }

        // Phase-appropriate notification
        var (title, message) = currentPhase switch
        {
            ExecutionPhase.Pickup => ("Your order is being processed", "Our driver is on the way to pick up your bags"),
            ExecutionPhase.DepartureCheckin => ("Your bags arrived at the airport", "Your luggage is now at the airport and being processed"),
            ExecutionPhase.ArrivalCheckin => ("Your bags cleared customs", "Our handler has received your bags at the destination"),
            ExecutionPhase.Delivery => ("Out for delivery", "Your bags are on their way to you"),
            _ => ("Order update", "Your order status has been updated")
        };

        _db.Notifications.Add(new Notification
        {
            UserId = order.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = title,
            Message = message,
            NotificationChannel = NotificationChannel.InApp,
            OrderId = order.OrderId
        });

        await _db.SaveChangesAsync();

        await _pusher.PushToCustomerAsync(order.CustomerId, title, message, "OrderUpdated", order.OrderId);

        return new TaskActionResponse
        {
            Success = true,
            OrderServiceId = orderServiceId,
            Status = "in_progress",
            StartedAt = now
        };
    }

    public async Task<TaskActionResponse> CompleteTaskAsync(int employeeId, int orderServiceId)
    {
        var employee = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        var os = await _db.OrderServices
            .Include(x => x.PackageService)
            .Include(x => x.Order).ThenInclude(o => o.Baggages)
            .Include(x => x.Order).ThenInclude(o => o.Package)
            .Include(x => x.Order).ThenInclude(o => o.OrderServices)
                .ThenInclude(s => s.PackageService)
            .FirstOrDefaultAsync(x => x.OrderServiceId == orderServiceId)
            ?? throw new KeyNotFoundException("Task not found");

        if (os.AssignedEmployeeId != employeeId)
            throw new UnauthorizedAccessException("Unauthorized");

        if (os.ServiceStatus != ServiceStatus.InProgress)
            throw new InvalidOperationException("Task not in progress");

        var currentPhase = os.PackageService?.ExecutionPhase;
        var order = os.Order;
        var baggageIds = order.Baggages.Select(b => b.BaggageId).ToList();

        // ===== Phase-aware validations =====
        if (currentPhase != ExecutionPhase.Tracking)
        {
            // All phases require scanning ALL bags (per this OrderService)
            var scannedCount = await _db.QrScans
                .Where(q => baggageIds.Contains(q.BaggageId) && q.OrderServiceId == orderServiceId)
                .Select(q => q.BaggageId)
                .Distinct()
                .CountAsync();

            if (scannedCount < baggageIds.Count)
                throw new InvalidOperationException($"All bags must be scanned. {baggageIds.Count - scannedCount} bags remaining.");

            // All phases require 3+ photos per bag (per this OrderService)
            var photoCounts = await _db.BaggagePhotos
                .Where(p => baggageIds.Contains(p.BaggageId) && p.OrderServiceId == orderServiceId)
                .GroupBy(p => p.BaggageId)
                .Select(g => new { BaggageId = g.Key, Count = g.Count() })
                .ToListAsync();

            var bagsWithInsufficientPhotos = baggageIds
                .Count(id => !photoCounts.Any(pc => pc.BaggageId == id && pc.Count >= 3));

            if (bagsWithInsufficientPhotos > 0)
                throw new InvalidOperationException($"At least 3 photos required per bag. {bagsWithInsufficientPhotos} bags need more photos.");

            // Pickup phase ONLY: Security Lock must be set
            if (currentPhase == ExecutionPhase.Pickup)
            {
                var unlockedBags = 0;
                foreach (var bagId in baggageIds)
                {
                    var hasLock = await _db.SecurityLocks
                        .AnyAsync(l => l.BaggageId == bagId && l.IsActive && !l.IsDeleted);
                    if (!hasLock) unlockedBags++;
                }
                if (unlockedBags > 0)
                    throw new InvalidOperationException($"Security lock must be set on all bags. {unlockedBags} bags need a lock.");
            }
        }

        var now = DateTime.UtcNow;
        os.ServiceStatus = ServiceStatus.Completed;
        os.UpdatedAt = now;
        // ActualEndTime will be set by the NEXT phase's StartTask, except for the last phase
        
        // ===== Tracking status updates =====
        if (currentPhase == ExecutionPhase.Pickup)
        {
            // PickedUp — all bags scanned, so mark them all
            foreach (var bagId in baggageIds)
            {
                _db.BaggageTrackings.Add(new BaggageTracking
                {
                    Status = BaggageTrackingStatus.PickedUp,
                    HandledByEmployeeId = employeeId,
                    BaggageId = bagId,
                    ArrivalTime = now
                });
            }
        }
        else if (currentPhase == ExecutionPhase.ArrivalCheckin)
        {
            // AtCustoms — bags cleared customs (Door To Door only, set by Travora)
            var packageName = order.Package?.PackageName;
            if (packageName == PackageNames.DoorToDoor)
            {
                foreach (var bagId in baggageIds)
                {
                    _db.BaggageTrackings.Add(new BaggageTracking
                    {
                        Status = BaggageTrackingStatus.AtCustoms,
                        HandledByEmployeeId = employeeId,
                        BaggageId = bagId,
                        ArrivalTime = now
                    });
                }
            }
        }
        else if (currentPhase == ExecutionPhase.Delivery)
        {
            // Delivered
            foreach (var bagId in baggageIds)
            {
                _db.BaggageTrackings.Add(new BaggageTracking
                {
                    Status = BaggageTrackingStatus.Delivered,
                    HandledByEmployeeId = employeeId,
                    BaggageId = bagId,
                    ArrivalTime = now
                });
            }
        }

        // ===== Auto-Assign Chain =====
        await AutoAssignNextPhaseAsync(os, order, currentPhase, now);

        // Check if all order services are completed
        var allCompleted = order.OrderServices.All(s =>
            s.OrderServiceId == orderServiceId || s.ServiceStatus == ServiceStatus.Completed);

        if (allCompleted)
        {
            os.ActualEndTime = now; // Last phase sets its own end time
            order.OrderStatus = OrderStatus.Completed;
            order.UpdatedAt = now;
        }

        // ===== Notifications =====
        if (allCompleted)
        {
            var packageName = order.Package?.PackageName ?? "";
            var (title, message) = packageName switch
            {
                PackageNames.DoorToDoor => ("Your bags have been delivered!", "Your luggage has been safely delivered to your address. Thank you for using Travora! 🎉"),
                PackageNames.CarServiceToAirport => ("Your bags are on the aircraft!", "Your luggage has been loaded on the aircraft. Have a safe flight! ✈️"),
                PackageNames.CarServiceFromAirport => ("Your bags have been delivered!", "Your luggage has been safely delivered to your address. Thank you for using Travora! 🎉"),
                _ => ("Order completed", "Your order has been completed successfully")
            };

            _db.Notifications.Add(new Notification
            {
                UserId = order.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.OrderCompleted,
                Title = title,
                Message = message,
                NotificationChannel = NotificationChannel.InApp,
                OrderId = order.OrderId
            });

            await _db.SaveChangesAsync();
            await _pusher.PushToCustomerAsync(order.CustomerId, title, message, "OrderCompleted", order.OrderId);
        }
        else
        {
            _db.Notifications.Add(new Notification
            {
                UserId = order.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.OrderUpdated,
                Title = "A stage of your order has been completed",
                Message = "The next stage is being processed",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = order.OrderId
            });

            await _db.SaveChangesAsync();
            await _pusher.PushToCustomerAsync(order.CustomerId, "A stage of your order has been completed", "The next stage is being processed", "OrderUpdated", order.OrderId);
        }

        return new TaskActionResponse
        {
            Success = true,
            OrderServiceId = orderServiceId,
            Status = "completed",
            CompletedAt = now,
            OrderCompleted = allCompleted
        };
    }

    // ===== Auto-Assign Next Phase =====
    private async Task AutoAssignNextPhaseAsync(OrderService currentOs, Order order, ExecutionPhase? currentPhase, DateTime now)
    {
        var nextPhase = currentPhase switch
        {
            ExecutionPhase.Pickup => ExecutionPhase.DepartureCheckin,
            ExecutionPhase.DepartureCheckin => ExecutionPhase.ArrivalCheckin,
            ExecutionPhase.ArrivalCheckin => ExecutionPhase.Delivery,
            _ => (ExecutionPhase?)null
        };

        if (!nextPhase.HasValue) return;

        var nextService = order.OrderServices
            .FirstOrDefault(s => s.PackageService?.ExecutionPhase == nextPhase.Value
                              && s.ServiceStatus == ServiceStatus.Pending);

        if (nextService == null) return;

        // Determine role needed for next phase
        var needsDriver = nextPhase is ExecutionPhase.Pickup or ExecutionPhase.Delivery;
        var needsHandler = nextPhase is ExecutionPhase.DepartureCheckin or ExecutionPhase.ArrivalCheckin;

        int? assignedId = null;

        if (needsHandler)
        {
            var handler = await _db.Employees
                .Where(e => e.JobRole == JobRole.BaggageHandler && e.IsActive && !e.IsDeleted)
                .Include(e => e.AssignedOrderServices)
                .FirstOrDefaultAsync(h => !h.AssignedOrderServices.Any(s =>
                    s.ServiceStatus == ServiceStatus.InProgress || s.ServiceStatus == ServiceStatus.Assigned));

            assignedId = handler?.EmployeeId;
        }
        else if (needsDriver)
        {
            var slotStart = nextService.ScheduledStartTime.TimeOfDay;
            var slotEnd = nextService.ScheduledEndTime.TimeOfDay;
            var date = nextService.ScheduledStartTime.Date;

            var driver = await _db.Employees
                .Where(e => e.JobRole == JobRole.Driver && e.IsActive && !e.IsDeleted && e.VehicleId != null)
                .Include(e => e.AssignedOrderServices)
                .ToListAsync();

            var available = driver.FirstOrDefault(d =>
                IsShiftCovering(d.ShiftType, slotStart, slotEnd) &&
                !HasConflict(d, date, slotStart, slotEnd));

            assignedId = available?.EmployeeId;
        }

        if (assignedId.HasValue)
        {
            nextService.AssignedEmployeeId = assignedId.Value;
            nextService.ServiceStatus = ServiceStatus.Assigned;
            nextService.AssignedAt = now;
            nextService.UpdatedAt = now;

            var phaseLabel = nextPhase switch
            {
                ExecutionPhase.DepartureCheckin => "airport check-in",
                ExecutionPhase.ArrivalCheckin => "arrival check-in",
                ExecutionPhase.Delivery => "delivery",
                _ => "task"
            };

            _db.Notifications.Add(new Notification
            {
                UserId = assignedId.Value,
                UserType = UserType.Employee,
                NotificationType = NotificationType.OrderUpdated,
                Title = $"New {phaseLabel} task assigned",
                Message = $"You have been assigned to handle a {phaseLabel} for order #{order.OrderId}",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = order.OrderId
            });

            await _pusher.PushToEmployeeAsync(assignedId.Value,
                $"New {phaseLabel} task assigned",
                $"You have been assigned to handle a {phaseLabel} for order #{order.OrderId}",
                "NewTaskAssigned", order.OrderId);
        }
    }

    public async Task<CompletedTasksResponse> GetCompletedTasksAsync(int employeeId, int page, int pageSize)
    {
        var query = _db.OrderServices
            .Where(os => os.AssignedEmployeeId == employeeId && os.ServiceStatus == ServiceStatus.Completed)
            .Include(os => os.Order).ThenInclude(o => o.Customer)
            .Include(os => os.Order).ThenInclude(o => o.PickupLocation)
            .Include(os => os.Order).ThenInclude(o => o.Baggages)
            .Include(os => os.PackageService).ThenInclude(ps => ps.Service)
            .OrderByDescending(os => os.ActualEndTime);

        var totalCompleted = await query.CountAsync();

        var tasks = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(os => new CompletedTaskItemDto
            {
                OrderServiceId = os.OrderServiceId,
                Type = os.PackageService.Service.ServiceName,
                Location = os.Order.PickupLocation.City + ", " + os.Order.PickupLocation.StreetAddress,
                ClientName = os.Order.Customer.Firstname + " " + os.Order.Customer.Lastname,
                ScheduledDate = os.ScheduledStartTime.ToString("dd/MM/yyyy"),
                ScheduledTime = os.ScheduledStartTime.ToString("hh:mm tt"),
                CompletedAt = os.ActualEndTime != null ? os.ActualEndTime.Value.ToString("dd/MM/yyyy hh:mm tt") : null,
                BaggageCount = os.Order.Baggages.Count
            })
            .ToListAsync();

        return new CompletedTasksResponse
        {
            TotalCompleted = totalCompleted,
            Tasks = tasks
        };
    }

    // ========================================================
    // GET /api/v1/employee/tasks/cancel-reasons
    // ========================================================
    public List<CancelReasonDto> GetCancelReasons() => CancelReasons;

    // ========================================================
    // PATCH /api/v1/employee/tasks/{orderServiceId}/cancel
    // ========================================================
    public async Task<EmployeeCancelTaskResponse> CancelTaskAsync(
        int employeeId, int orderServiceId, EmployeeCancelTaskRequest request)
    {
        // Validate reason
        var reason = CancelReasons.FirstOrDefault(r => r.Id == request.ReasonId);
        if (reason == null)
            return new EmployeeCancelTaskResponse { Success = false, Message = "Invalid cancellation reason" };

        var os = await _db.OrderServices
            .Include(x => x.PackageService).ThenInclude(ps => ps.Service)
            .Include(x => x.Order).ThenInclude(o => o.OrderServices)
            .Include(x => x.Order).ThenInclude(o => o.Invoices)
            .Include(x => x.Order).ThenInclude(o => o.CustomsDeclarations)
            .Include(x => x.Order).ThenInclude(o => o.Customer)
            .FirstOrDefaultAsync(x => x.OrderServiceId == orderServiceId);

        if (os == null)
            return new EmployeeCancelTaskResponse { Success = false, Message = "Task not found" };

        if (os.AssignedEmployeeId != employeeId)
            return new EmployeeCancelTaskResponse { Success = false, Message = "Unauthorized" };

        if (os.ServiceStatus != ServiceStatus.InProgress)
            return new EmployeeCancelTaskResponse { Success = false, Message = "Task must be in progress to cancel" };

        var currentPhase = os.PackageService?.ExecutionPhase;
        if (currentPhase != ExecutionPhase.ArrivalCheckin)
            return new EmployeeCancelTaskResponse { Success = false, Message = "Only arrival/customs tasks can be cancelled by employee" };

        var now = DateTime.UtcNow;
        var order = os.Order;
        var customerId = order.CustomerId;
        var customerName = $"{order.Customer.Firstname} {order.Customer.Lastname}";

        // ===== Cancel the order and all pending/assigned services =====
        order.OrderStatus = OrderStatus.Cancelled;
        order.CancellationReason = $"[Employee] {reason.Title}" + (string.IsNullOrEmpty(request.Notes) ? "" : $" - {request.Notes}");
        order.UpdatedAt = now;

        foreach (var svc in order.OrderServices)
        {
            if (svc.ServiceStatus is ServiceStatus.Pending or ServiceStatus.Assigned or ServiceStatus.InProgress)
            {
                svc.ServiceStatus = ServiceStatus.Cancelled;
                svc.UpdatedAt = now;
            }
        }

        // ===== Refund logic based on reason =====
        decimal refundAmount = 0;
        string? refundType = null;
        bool refundSuccess = false;

        if (request.ReasonId == 1) // Customs mismatch — refund customs fees ONLY
        {
            var declaration = order.CustomsDeclarations.FirstOrDefault();
            if (declaration != null && declaration.TotalCustomsFee > 0)
            {
                refundAmount = declaration.TotalCustomsFee;
                refundType = "customs_fees_only";

                // Save order cancellation first so the refund service can process it
                await _db.SaveChangesAsync();

                // Execute partial refund through Paymob immediately
                var refundResult = await _refundService.ProcessEmployeeRefundAsync(
                    order.OrderId, refundAmount, $"Customs fee refund: {reason.Title}");

                refundSuccess = refundResult.Success;
            }
        }
        // ReasonId == 2 (No declaration) — NO refund at all, customer's fault

        // ===== Notification to customer =====
        var notifTitle = "Order Cancelled";
        var notifMessage = request.ReasonId == 1
            ? $"Your order has been cancelled due to an incorrect customs declaration. "
              + $"The customs fees of {refundAmount:F2} EGP will be refunded. "
              + "Please bring your passport and collect your bags from the airport."
            : "Your order has been cancelled because undeclared customs items were found. "
              + "Please bring your passport and collect your bags from the airport.";

        _db.Notifications.Add(new Notification
        {
            UserId = customerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = notifTitle,
            Message = notifMessage,
            NotificationChannel = NotificationChannel.InApp,
            OrderId = order.OrderId
        });

        // ===== Notify assigned employees on other services =====
        var otherAssigned = order.OrderServices
            .Where(s => s.OrderServiceId != orderServiceId && s.AssignedEmployeeId.HasValue)
            .ToList();

        foreach (var svc in otherAssigned)
        {
            var empId = svc.AssignedEmployeeId!.Value;
            _db.Notifications.Add(new Notification
            {
                UserId = empId,
                UserType = UserType.Employee,
                NotificationType = NotificationType.OrderUpdated,
                Title = "Order Cancelled",
                Message = $"Order #{order.OrderId} has been cancelled by customs handler",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = order.OrderId
            });
            await _pusher.PushToEmployeeAsync(empId, "Order Cancelled",
                $"Order #{order.OrderId} has been cancelled by customs handler",
                "OrderCancelled", order.OrderId);
        }

        await _db.SaveChangesAsync();

        // Push notification to customer
        await _pusher.PushToCustomerAsync(customerId, notifTitle, notifMessage, "OrderCancelled", order.OrderId);

        return new EmployeeCancelTaskResponse
        {
            Success = true,
            Message = request.ReasonId == 1
                ? refundSuccess
                    ? $"Order cancelled. Customs fees of {refundAmount:F2} EGP have been refunded."
                    : $"Order cancelled. Customs fee refund of {refundAmount:F2} EGP failed — admin has been notified."
                : "Order cancelled. No refund issued.",
            RefundAmount = refundAmount,
            RefundType = refundType
        };
    }

    // ===== Helper Methods =====
    private bool IsShiftCovering(ShiftType shift, TimeSpan slotStart, TimeSpan slotEnd)
    {
        return shift switch
        {
            ShiftType.Morning => slotStart >= TimeSpan.FromHours(8) && slotEnd <= TimeSpan.FromHours(16),
            ShiftType.Evening => slotStart >= TimeSpan.FromHours(16) && slotEnd <= TimeSpan.FromHours(24),
            ShiftType.Night => (slotStart >= TimeSpan.FromHours(22) || slotStart < TimeSpan.FromHours(8)), 
            ShiftType.rotating => true,
            _ => false
        };
    }

    private bool HasConflict(Employee driver, DateTime date, TimeSpan slotStart, TimeSpan slotEnd)
    {
        return driver.AssignedOrderServices.Any(os =>
            os.ScheduledStartTime.Date == date &&
            os.ScheduledStartTime.TimeOfDay < slotEnd &&
            os.ScheduledEndTime.TimeOfDay > slotStart &&
            os.ServiceStatus != ServiceStatus.Completed);
    }
}
