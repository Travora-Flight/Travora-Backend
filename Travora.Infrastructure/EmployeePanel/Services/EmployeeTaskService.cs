using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Employee.Tasks;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Employee;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.EmployeePanel.Services;

public class EmployeeTaskService : IEmployeeTaskService
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationPusher _pusher;

    public EmployeeTaskService(ApplicationDbContext db, INotificationPusher pusher)
    {
        _db = db;
        _pusher = pusher;
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

        // Query scanned baggage IDs from QrScans table
        var orderBaggageIds = order.Baggages.Select(b => b.BaggageId).ToList();
        var scannedBaggageIds = await _db.QrScans
            .Where(q => orderBaggageIds.Contains(q.BaggageId))
            .Select(q => q.BaggageId)
            .Distinct()
            .ToListAsync();

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
                            PhotosCount = b.BaggagePhotos.Count
                        };
                    }).ToList()
                };
            }).ToList();

        return new TaskDetailResponse
        {
            OrderServiceId = os.OrderServiceId,
            Status = os.ServiceStatus.ToString(),
            CanStart = canStart,
            ScheduledDate = os.ScheduledStartTime.ToString("dd/MM"),
            ScheduledTime = os.ScheduledStartTime.ToString("hh:mm tt"),
            Type = os.PackageService.Service.ServiceName,
            Location = $"{location.City}, {location.StreetAddress}",
            GpsLatitude = location.GpsLatitude,
            GpsLongitude = location.GpsLongitude,
            MapUrl = $"https://maps.google.com/?q={location.GpsLatitude},{location.GpsLongitude}",
            ClientInfo = new ClientInfoDto
            {
                Name = $"{order.Customer.Firstname} {order.Customer.Lastname}",
                Mobile = order.Customer.PhoneNumber
            },
            TotalBaggageCount = order.TotalBaggageCount,
            ScannedCount = scannedBaggageIds.Count,
            Bags = groupedBags
        };
    }

    public async Task<TaskActionResponse> StartTaskAsync(int employeeId, int orderServiceId)
    {
        var os = await _db.OrderServices
            .Include(x => x.Order)
            .FirstOrDefaultAsync(x => x.OrderServiceId == orderServiceId)
            ?? throw new KeyNotFoundException("Task not found");

        if (os.AssignedEmployeeId != employeeId)
            throw new UnauthorizedAccessException("Unauthorized");

        if (os.ServiceStatus != ServiceStatus.Assigned)
            throw new InvalidOperationException("Task not assigned yet or already started");

        var now = DateTime.UtcNow;
        if (os.ScheduledStartTime > now.AddMinutes(30))
            throw new InvalidOperationException("The order time has not arrived yet");

        os.ServiceStatus = ServiceStatus.InProgress;
        os.ActualStartTime = now;
        os.UpdatedAt = now;

        // Update order status if this is the first service to start
        var order = os.Order;
        if (order.OrderStatus == OrderStatus.Pending)
        {
            order.OrderStatus = OrderStatus.InProgress;
            order.UpdatedAt = now;
        }

        // Notification
        _db.Notifications.Add(new Notification
        {
            UserId = order.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = "Your order is being processed",
            Message = "The employee is on the way to you",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = order.OrderId
        });

        await _db.SaveChangesAsync();

        await _pusher.PushToCustomerAsync(
            order.CustomerId,
            "Your order is being processed",
            "The employee is on the way to you",
            "OrderUpdated",
            order.OrderId);

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
            .Include(x => x.Order).ThenInclude(o => o.Baggages).ThenInclude(b => b.BaggagePhotos)
            .Include(x => x.Order).ThenInclude(o => o.OrderServices)
                .ThenInclude(s => s.PackageService)
            .FirstOrDefaultAsync(x => x.OrderServiceId == orderServiceId)
            ?? throw new KeyNotFoundException("Task not found");

        if (os.AssignedEmployeeId != employeeId)
            throw new UnauthorizedAccessException("Unauthorized");

        if (os.ServiceStatus != ServiceStatus.InProgress)
            throw new InvalidOperationException("Task not in progress");

        // Driver validations
        if (employee.JobRole == JobRole.Driver)
        {
            var completeBaggageIds = os.Order.Baggages.Select(b => b.BaggageId).ToList();
            var completeScannedIds = await _db.QrScans
                .Where(q => completeBaggageIds.Contains(q.BaggageId))
                .Select(q => q.BaggageId)
                .Distinct()
                .ToListAsync();
            var unscannedBags = completeBaggageIds.Count - completeScannedIds.Count;
            if (unscannedBags > 0)
                throw new InvalidOperationException("All bags must be scanned before completion");

            var bagsWithoutPhotos = os.Order.Baggages.Count(b => b.BaggagePhotos.Count < 3);
            if (bagsWithoutPhotos > 0)
                throw new InvalidOperationException("Photos must be uploaded for all bags");
        }

        var now = DateTime.UtcNow;
        os.ServiceStatus = ServiceStatus.Completed;
        os.ActualEndTime = now;
        os.UpdatedAt = now;

        var order = os.Order;
        var executionPhase = os.PackageService?.ExecutionPhase;

        // ===== Auto-Assign Chain =====
        if (executionPhase == ExecutionPhase.Pickup)
        {
            // Pickup completed → auto-assign AirportCheckin to first available BaggageHandler
            var airportCheckinService = order.OrderServices
                .FirstOrDefault(s =>
                    s.PackageService?.ExecutionPhase == ExecutionPhase.AirportCheckin
                    && s.ServiceStatus == ServiceStatus.Pending);

            if (airportCheckinService != null)
            {
                var handlers = await _db.Employees
                    .Where(e => e.JobRole == JobRole.BaggageHandler
                             && e.IsActive
                             && !e.IsDeleted)
                    .Include(e => e.AssignedOrderServices)
                    .ToListAsync();

                var availableHandler = handlers.FirstOrDefault(h =>
                    !h.AssignedOrderServices.Any(s =>
                        s.ServiceStatus == ServiceStatus.InProgress ||
                        s.ServiceStatus == ServiceStatus.Assigned));

                if (availableHandler != null)
                {
                    airportCheckinService.AssignedEmployeeId = availableHandler.EmployeeId;
                    airportCheckinService.ServiceStatus = ServiceStatus.Assigned;
                    airportCheckinService.AssignedAt = now;
                    airportCheckinService.UpdatedAt = now;

                    _db.Notifications.Add(new Notification
                    {
                        UserId = availableHandler.EmployeeId,
                        UserType = UserType.Employee,
                        NotificationType = NotificationType.OrderUpdated,
                        Title = "You have been assigned to a new request",
                        Message = "Please receive the bags from the driver at the Check-in point",
                        NotificationChannel = NotificationChannel.InApp,
                        OrderId = order.OrderId
                    });

                    await _pusher.PushToEmployeeAsync(
                        availableHandler.EmployeeId,
                        "You have been assigned to a new request",
                        "Please receive the bags from the driver at the Check-in point",
                        "NewTaskAssigned",
                        order.OrderId);

                    // Customer notification — bags at airport
                    _db.Notifications.Add(new Notification
                    {
                        UserId = order.CustomerId,
                        UserType = UserType.Customer,
                        NotificationType = NotificationType.OrderUpdated,
                        Title = "Your bags arrived at the airport",
                        Message = "Your luggage is now at the airport and being processed",
                        NotificationChannel = NotificationChannel.InApp,
                        OrderId = order.OrderId
                    });

                    await _pusher.PushToCustomerAsync(
                        order.CustomerId,
                        "Your bags arrived at the airport",
                        "Your luggage is now at the airport and being processed",
                        "OrderUpdated",
                        order.OrderId);
                }
            }
        }
        else if (executionPhase == ExecutionPhase.AirportCheckin)
        {
            // AirportCheckin completed → auto-assign Delivery to first available Driver
            var deliveryService = order.OrderServices
                .FirstOrDefault(s =>
                    s.PackageService?.ExecutionPhase == ExecutionPhase.Delivery
                    && s.ServiceStatus == ServiceStatus.Pending);

            if (deliveryService != null)
            {
                var drivers = await _db.Employees
                    .Where(e => e.JobRole == JobRole.Driver
                             && e.IsActive
                             && !e.IsDeleted)
                    .Include(e => e.AssignedOrderServices)
                    .ToListAsync();

                var slotStart = deliveryService.ScheduledStartTime.TimeOfDay;
                var slotEnd = deliveryService.ScheduledEndTime.TimeOfDay;
                var date = deliveryService.ScheduledStartTime.Date;

                var availableDriver = drivers.FirstOrDefault(d =>
                    IsShiftCovering(d.ShiftType, slotStart, slotEnd) &&
                    !HasConflict(d, date, slotStart, slotEnd));

                if (availableDriver != null)
                {
                    deliveryService.AssignedEmployeeId = availableDriver.EmployeeId;
                    deliveryService.ServiceStatus = ServiceStatus.Assigned;
                    deliveryService.AssignedAt = now;
                    deliveryService.UpdatedAt = now;

                    _db.Notifications.Add(new Notification
                    {
                        UserId = availableDriver.EmployeeId,
                        UserType = UserType.Employee,
                        NotificationType = NotificationType.OrderUpdated,
                        Title = "You have been assigned to a new delivery",
                        Message = "Please receive the bags from the airport and deliver them to the customer",
                        NotificationChannel = NotificationChannel.InApp,
                        OrderId = order.OrderId
                    });

                    await _pusher.PushToEmployeeAsync(
                        availableDriver.EmployeeId,
                        "You have been assigned to a new delivery",
                        "Please receive the bags from the airport and deliver them to the customer",
                        "NewTaskAssigned",
                        availableDriver.EmployeeId);

                    // Customer notification — delivery driver assigned
                    _db.Notifications.Add(new Notification
                    {
                        UserId = order.CustomerId,
                        UserType = UserType.Customer,
                        NotificationType = NotificationType.OrderUpdated,
                        Title = "Delivery driver assigned",
                        Message = "Your bags are on the way to you",
                        NotificationChannel = NotificationChannel.InApp,
                        OrderId = order.OrderId
                    });

                    await _pusher.PushToCustomerAsync(
                        order.CustomerId,
                        "Delivery driver assigned",
                        "Your bags are on the way to you",
                        "OrderUpdated",
                        order.OrderId);
                }
                // If no driver -> remains Pending and Admin assigns manually
            }
        }

        // Check if all order services are completed
        var allCompleted = order.OrderServices.All(s =>
            s.OrderServiceId == orderServiceId || s.ServiceStatus == ServiceStatus.Completed);

        if (allCompleted)
        {
            order.OrderStatus = OrderStatus.Completed;
            order.UpdatedAt = now;
        }

        // Notification to customer
        _db.Notifications.Add(new Notification
        {
            UserId = order.CustomerId,
            UserType = UserType.Customer,
            NotificationType = allCompleted ? NotificationType.OrderCompleted : NotificationType.OrderUpdated,
            Title = allCompleted ? "Your order has been fully completed" : "A stage of your order has been completed",
            Message = allCompleted ? "Your bag has been successfully delivered ✅" : "The next stage is being processed",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = order.OrderId
        });

        await _db.SaveChangesAsync();

        if (allCompleted)
        {
            await _pusher.PushToCustomerAsync(
                order.CustomerId,
                "Your order has been fully completed",
                "Your bag has been successfully delivered ✅",
                "OrderCompleted",
                order.OrderId);
        }
        else
        {
            await _pusher.PushToCustomerAsync(
                order.CustomerId,
                "A stage of your order has been completed",
                "The next stage is being processed",
                "OrderUpdated",
                order.OrderId);
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
