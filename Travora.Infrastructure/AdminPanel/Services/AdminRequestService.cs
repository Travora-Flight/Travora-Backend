using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Requests;
using Travora.Application.Interfaces;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminRequestService : IAdminRequestService
{
    private readonly ApplicationDbContext _db;

    public AdminRequestService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<RequestPagedResponse> GetRequestsAsync(string? search, string? filter, string? status, int page, int pageSize)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderServices)
                .ThenInclude(os => os.AssignedEmployee)
            .AsQueryable();

        // 1. Search
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(o => 
                (o.Customer != null && (o.Customer.Firstname.ToLower().Contains(searchLower) || o.Customer.Lastname.ToLower().Contains(searchLower))) ||
                o.OrderId.ToString().Contains(searchLower));
        }

        // 2. Filter (Time)
        var now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            if (filter.Equals("today", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(o => o.CreatedAt.Date == now.Date);
            }
            else if (filter.Equals("this_week", StringComparison.OrdinalIgnoreCase))
            {
                var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
                query = query.Where(o => o.CreatedAt.Date >= startOfWeek);
            }
        }

        // 3. Status
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(o => o.OrderStatus == parsedStatus);
            }
        }

        var total = await query.CountAsync();

        var requests = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new RequestListResponse
            {
                OrderId = o.OrderId,
                ClientName = o.Customer != null ? $"{o.Customer.Firstname} {o.Customer.Lastname}" : "Unknown",
                Type = o.PackageId > 0 ? "package_service" : "service",
                Status = o.OrderStatus.ToString().ToLower(),
                AssignedEmployee = o.OrderServices.FirstOrDefault(os => os.AssignedEmployee != null) != null
                    ? (o.OrderServices.FirstOrDefault(os => os.AssignedEmployee != null)!.AssignedEmployee!.Firstname + " " + o.OrderServices.FirstOrDefault(os => os.AssignedEmployee != null)!.AssignedEmployee!.Lastname)
                    : "Not Assigned",
                Time = o.CreatedAt.ToString("hh:mm tt")
            })
            .ToListAsync();

        return new RequestPagedResponse
        {
            Requests = requests,
            Total = total
        };
    }

    public async Task<RequestDetailResponse> GetRequestDetailsAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.PickupLocation)
            .Include(o => o.DeliveryLocation)
            .Include(o => o.OrderServices)
                .ThenInclude(os => os.AssignedEmployee)
            .Include(o => o.OrderServices)
                .ThenInclude(os => os.PackageService)
                    .ThenInclude(ps => ps.Service)
            .FirstOrDefaultAsync(o => o.OrderId == orderId)
            ?? throw new KeyNotFoundException("Order not found");

        var assignedEmp = order.OrderServices.FirstOrDefault(os => os.AssignedEmployee != null)?.AssignedEmployee;
        
        // Mock timeline based on real data
        var timeline = new List<TimelineItem>();
        timeline.Add(new TimelineItem { Event = "Request Sent", Time = order.CreatedAt.ToString("hh:mm tt"), IsDone = true });
        
        if (assignedEmp != null)
        {
            timeline.Add(new TimelineItem { Event = "Assign Employee", Time = order.UpdatedAt?.ToString("hh:mm tt") ?? order.CreatedAt.ToString("hh:mm tt"), IsDone = true });
            
            if (order.OrderStatus == OrderStatus.InProgress || order.OrderStatus == OrderStatus.Completed)
                timeline.Add(new TimelineItem { Event = "Begin to Execute", Time = order.UpdatedAt?.ToString("hh:mm tt") ?? order.CreatedAt.ToString("hh:mm tt"), IsDone = true });
            else
                timeline.Add(new TimelineItem { Event = "Begin to Execute", Time = null, IsDone = false });
        }
        else
        {
            timeline.Add(new TimelineItem { Event = "Assign Employee", Time = null, IsDone = false });
            timeline.Add(new TimelineItem { Event = "Begin to Execute", Time = null, IsDone = false });
        }

        if (order.OrderStatus == OrderStatus.Completed)
            timeline.Add(new TimelineItem { Event = "Request Done", Time = order.UpdatedAt?.ToString("hh:mm tt") ?? order.CreatedAt.ToString("hh:mm tt"), IsDone = true });
        else
            timeline.Add(new TimelineItem { Event = "Request Done", Time = null, IsDone = false });

        return new RequestDetailResponse
        {
            OrderId = order.OrderId,
            Status = order.OrderStatus.ToString().ToLower(),
            ClientInfo = new ClientInfo
            {
                Name = order.Customer != null ? $"{order.Customer.Firstname} {order.Customer.Lastname}" : "Unknown",
                Mobile = order.Customer?.PhoneNumber ?? string.Empty,
                Address = order.PickupLocation?.StreetAddress ?? string.Empty,
                MapUrl = $"https://maps.google.com/?q={order.PickupLocation?.GpsLatitude},{order.PickupLocation?.GpsLongitude}"
            },
            ServiceDetails = new ServiceDetails
            {
                ServiceType = order.PackageId > 0 ? "Package" : (order.OrderServices.FirstOrDefault()?.PackageService?.Service?.ServiceName ?? "Unknown Service"),
                AssignedEmployee = assignedEmp != null ? $"{assignedEmp.Firstname} {assignedEmp.Lastname}" : "Not Assigned"
            },
            Timeline = timeline
        };
    }

    public async Task<bool> AssignEmployeeAsync(int orderId, AssignEmployeeRequest request)
    {
        var order = await _db.Orders
            .Include(o => o.OrderServices)
            .FirstOrDefaultAsync(o => o.OrderId == orderId)
            ?? throw new KeyNotFoundException("Order not found");

        var employee = await _db.Employees.FindAsync(request.EmployeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        // لو فيه OrderServiceId محدد → assign لخدمة معينة
        if (request.OrderServiceId.HasValue)
        {
            var targetService = order.OrderServices
                .FirstOrDefault(s => s.OrderServiceId == request.OrderServiceId.Value)
                ?? throw new KeyNotFoundException("Order service not found");

            if (targetService.ServiceStatus != ServiceStatus.Pending)
                throw new InvalidOperationException("هذه الخدمة مش في حالة Pending");

            targetService.AssignedEmployeeId = request.EmployeeId;
            targetService.ServiceStatus = ServiceStatus.Assigned;
            targetService.AssignedAt = DateTime.UtcNow;
            targetService.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Fallback: assign لأول خدمة Pending
            var pendingService = order.OrderServices
                .FirstOrDefault(s => s.ServiceStatus == ServiceStatus.Pending)
                ?? throw new InvalidOperationException("لا توجد خدمات في حالة Pending");

            pendingService.AssignedEmployeeId = request.EmployeeId;
            pendingService.ServiceStatus = ServiceStatus.Assigned;
            pendingService.AssignedAt = DateTime.UtcNow;
            pendingService.UpdatedAt = DateTime.UtcNow;
        }

        // Notification
        _db.Notifications.Add(new Domain.Entities.Notification
        {
            UserId = request.EmployeeId,
            UserType = UserType.Employee,
            NotificationType = NotificationType.OrderUpdated,
            Title = "تم تعيينك على مهمة جديدة",
            Message = "تم تعيينك يدوياً من قِبَل الإدارة",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = order.OrderId
        });

        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }
}
