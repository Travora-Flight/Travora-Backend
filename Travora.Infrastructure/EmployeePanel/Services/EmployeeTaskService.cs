using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Employee.Tasks;
using Travora.Application.Interfaces.Services.Employee;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.EmployeePanel.Services;

public class EmployeeTaskService : IEmployeeTaskService
{
    private readonly ApplicationDbContext _db;

    public EmployeeTaskService(ApplicationDbContext db)
    {
        _db = db;
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
            throw new UnauthorizedAccessException("مش مسموح");

        var now = DateTime.UtcNow;
        var canStart = os.ServiceStatus == ServiceStatus.Pending
            && os.ScheduledStartTime <= now.AddMinutes(30);

        var order = os.Order;
        var location = order.PickupLocation;

        var bags = order.Baggages.Select(b =>
        {
            var lastTracking = b.BaggageTrackings
                .OrderByDescending(t => t.ArrivalTime)
                .FirstOrDefault();

            BagOwnerDto? owner = null;
            if (b.BaggageNumber != null)
            {
                if (b.Customer != null)
                    owner = new BagOwnerDto { OwnerType = "customer", OwnerName = $"{b.Customer.Firstname} {b.Customer.Lastname}" };
                else if (b.Companion != null)
                    owner = new BagOwnerDto { OwnerType = "companion", OwnerName = $"{b.Companion.Firstname} {b.Companion.Lastname}" };
            }

            return new TaskBagItemDto
            {
                BaggageId = b.BaggageId,
                TagNumber = b.BaggageNumber,
                WeightKg = b.TotalWeight,
                CurrentStatus = lastTracking?.Status.ToString(),
                IsScanned = b.BaggageNumber != null,
                PhotosCount = b.BaggagePhotos.Count,
                Owner = owner
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
            ScannedCount = bags.Count(b => b.IsScanned),
            Bags = bags
        };
    }

    public async Task<TaskActionResponse> StartTaskAsync(int employeeId, int orderServiceId)
    {
        var os = await _db.OrderServices
            .Include(x => x.Order)
            .FirstOrDefaultAsync(x => x.OrderServiceId == orderServiceId)
            ?? throw new KeyNotFoundException("Task not found");

        if (os.AssignedEmployeeId != employeeId)
            throw new UnauthorizedAccessException("مش مسموح");

        if (os.ServiceStatus != ServiceStatus.Pending)
            throw new InvalidOperationException("Task already started");

        var now = DateTime.UtcNow;
        if (os.ScheduledStartTime > now.AddMinutes(30))
            throw new InvalidOperationException("لسه مجاش وقت الطلب");

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
            Title = "جاري تنفيذ طلبك",
            Message = "الموظف في الطريق إليك",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = order.OrderId
        });

        await _db.SaveChangesAsync();

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
            .Include(x => x.Order).ThenInclude(o => o.Baggages).ThenInclude(b => b.BaggagePhotos)
            .Include(x => x.Order).ThenInclude(o => o.OrderServices)
            .FirstOrDefaultAsync(x => x.OrderServiceId == orderServiceId)
            ?? throw new KeyNotFoundException("Task not found");

        if (os.AssignedEmployeeId != employeeId)
            throw new UnauthorizedAccessException("مش مسموح");

        if (os.ServiceStatus != ServiceStatus.InProgress)
            throw new InvalidOperationException("Task not in progress");

        // Driver validations
        if (employee.JobRole == JobRole.Driver)
        {
            var unscannedBags = os.Order.Baggages.Count(b => b.BaggageNumber == null);
            if (unscannedBags > 0)
                throw new InvalidOperationException("يجب سكان كل الشنط قبل الإكمال");

            var bagsWithoutPhotos = os.Order.Baggages.Count(b => b.BaggagePhotos.Count < 3);
            if (bagsWithoutPhotos > 0)
                throw new InvalidOperationException("يجب رفع صور لكل الشنط");
        }

        var now = DateTime.UtcNow;
        os.ServiceStatus = ServiceStatus.Completed;
        os.ActualEndTime = now;
        os.UpdatedAt = now;

        // Check if all order services are completed
        var order = os.Order;
        var allCompleted = order.OrderServices.All(s =>
            s.OrderServiceId == orderServiceId || s.ServiceStatus == ServiceStatus.Completed);

        if (allCompleted)
        {
            order.OrderStatus = OrderStatus.Completed;
            order.UpdatedAt = now;
        }

        // Notification
        _db.Notifications.Add(new Notification
        {
            UserId = order.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = "تم تنفيذ طلبك بنجاح",
            Message = "تم استلام شنطتك بنجاح",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = order.OrderId
        });

        await _db.SaveChangesAsync();

        return new TaskActionResponse
        {
            Success = true,
            OrderServiceId = orderServiceId,
            Status = "completed",
            CompletedAt = now,
            OrderCompleted = allCompleted
        };
    }
}
