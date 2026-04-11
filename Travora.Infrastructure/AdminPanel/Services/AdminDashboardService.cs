using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Travora.Application.DTOs.Admin.Dashboard;
using Travora.Application.Interfaces;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly ApplicationDbContext _db;
    private readonly IUpstashRedisService _redis;

    public AdminDashboardService(ApplicationDbContext db, IUpstashRedisService redis)
    {
        _db = db;
        _redis = redis;
    }

    public async Task<DashboardStatsResponse> GetDashboardStatsAsync()
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);

        var allEmployees = await _db.Employees.CountAsync();
        var newRequests = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Pending);
        var currentRequests = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.InProgress);
        var doneRequests = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Completed);

        // نشاط آخر 7 أيام
        var ordersLast7Days = await _db.Orders
            .Where(o => o.CreatedAt >= sevenDaysAgo)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Completed = g.Count(o => o.OrderStatus == OrderStatus.Completed),
                NewReqs = g.Count(o => o.CreatedAt.Date == g.Key),
                Ongoing = g.Count(o => o.OrderStatus == OrderStatus.InProgress)
            })
            .ToListAsync();

        var weeklyActivity = new List<WeeklyActivityItem>();
        for (int i = 6; i >= 0; i--)
        {
            var day = now.Date.AddDays(-i);
            var stats = ordersLast7Days.FirstOrDefault(o => o.Date == day);
            weeklyActivity.Add(new WeeklyActivityItem
            {
                Day = day.ToString("ddd"),
                Completed = stats?.Completed ?? 0,
                NewRequests = stats?.NewReqs ?? 0,
                Ongoing = stats?.Ongoing ?? 0
            });
        }

        return new DashboardStatsResponse
        {
            AllEmployees = allEmployees,
            AllEmployeesGrowth = 0,
            NewRequests = newRequests,
            NewRequestsGrowth = 0,
            CurrentRequests = currentRequests,
            CurrentRequestsChange = 0,
            DoneRequests = doneRequests,
            DoneRequestsGrowth = 0,
            WeeklyActivity = weeklyActivity
        };
    }

    // ===== Online Employees =====
    public async Task<OnlineEmployeesResponse> GetOnlineEmployeesAsync()
    {
        var employees = new List<OnlineEmployeeDetail>();
        var keys = await _redis.KeysAsync("employee:*:last_location");

        if (!keys.Any())
            return new OnlineEmployeesResponse { OnlineCount = 0, Employees = employees };

        var employeeIds = new List<int>();
        foreach (var key in keys)
        {
            var parts = key.ToString().Split(':');
            if (parts.Length == 3 && int.TryParse(parts[1], out int empId))
                employeeIds.Add(empId);
        }

        var dbEmployees = await _db.Employees
            .Where(e => employeeIds.Contains(e.EmployeeId))
            .Select(e => new { e.EmployeeId, e.Firstname, e.Lastname, e.ProfileImagePath })
            .ToListAsync();

        foreach (var emp in dbEmployees)
        {
            var key = $"employee:{emp.EmployeeId}:last_location";
            var locationData = await _redis.GetAsync(key);
            if (string.IsNullOrEmpty(locationData)) continue;

            try
            {
                using var doc = JsonDocument.Parse(locationData.ToString());
                var root = doc.RootElement;

                decimal lat = 0, lng = 0;
                string status = "available";
                string? currentTask = null;
                string lastUpdated = "Just now";

                if (root.TryGetProperty("latitude", out var latProp)) lat = latProp.GetDecimal();
                if (root.TryGetProperty("longitude", out var lngProp)) lng = lngProp.GetDecimal();
                if (root.TryGetProperty("status", out var statusProp)) status = statusProp.GetString() ?? "available";
                if (root.TryGetProperty("updatedAt", out var updatedProp))
                    lastUpdated = FormatTimeAgo(updatedProp.GetDateTime());

                // لو on_service، اجيب الـ current task
                if (status == "on_service")
                {
                    var currentOrder = await _db.OrderServices
                        .Include(os => os.PackageService).ThenInclude(ps => ps.Service)
                        .Include(os => os.Order).ThenInclude(o => o.PickupLocation)
                        .Where(os => os.AssignedEmployeeId == emp.EmployeeId && os.ServiceStatus == ServiceStatus.InProgress)
                        .FirstOrDefaultAsync();

                    if (currentOrder != null)
                        currentTask = $"{currentOrder.PackageService?.Service?.ServiceName ?? "Service"} - {currentOrder.Order?.PickupLocation?.City ?? "Unknown"}";
                }

                employees.Add(new OnlineEmployeeDetail
                {
                    EmployeeId = emp.EmployeeId,
                    Name = $"{emp.Firstname} {emp.Lastname}",
                    Code = $"EMP{emp.EmployeeId:D3}",
                    ProfileImageUrl = emp.ProfileImagePath,
                    Latitude = lat,
                    Longitude = lng,
                    Status = status,
                    CurrentTask = currentTask,
                    LastUpdated = lastUpdated
                });
            }
            catch { /* ignore parsing errors */ }
        }

        // ترتيب: on_service الأول، available التاني
        employees = employees
            .OrderByDescending(e => e.Status == "on_service")
            .ThenByDescending(e => e.Status == "available")
            .ToList();

        return new OnlineEmployeesResponse
        {
            OnlineCount = employees.Count,
            Employees = employees
        };
    }

    // ===== Recent Orders =====
    public async Task<RecentOrdersResponse> GetRecentOrdersAsync(int take = 10)
    {
        var orders = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Package)
            .Include(o => o.OrderServices).ThenInclude(os => os.AssignedEmployee)
            .OrderByDescending(o => o.CreatedAt)
            .Take(take)
            .ToListAsync();

        var items = orders.Select(o =>
        {
            var lastService = o.OrderServices
                .OrderByDescending(os => os.CreatedAt)
                .FirstOrDefault();

            var (status, statusCode) = MapOrderStatus(o.OrderStatus);

            return new RecentOrderItem
            {
                OrderId = o.OrderId,
                ClientName = o.Customer != null ? $"{o.Customer.Firstname} {o.Customer.Lastname}" : "Unknown",
                Type = o.Package?.PackageName ?? "Service",
                Status = status,
                StatusCode = statusCode,
                EmployeeName = lastService?.AssignedEmployee != null
                    ? $"{lastService.AssignedEmployee.Firstname} {lastService.AssignedEmployee.Lastname}"
                    : null,
                Time = o.CreatedAt.ToString("hh:mm tt"),
                Date = o.CreatedAt.ToString("dd/MM")
            };
        }).ToList();

        return new RecentOrdersResponse { Orders = items };
    }

    // ===== Live Locations =====
    public async Task<LiveLocationsResponse> GetLiveLocationsAsync()
    {
        var drivers = new List<LiveDriverItem>();
        var keys = await _redis.KeysAsync("employee:*:last_location");

        if (!keys.Any())
            return new LiveLocationsResponse { ActiveCount = 0, Drivers = drivers };

        var employeeIds = new List<int>();
        foreach (var key in keys)
        {
            var parts = key.ToString().Split(':');
            if (parts.Length == 3 && int.TryParse(parts[1], out int empId))
                employeeIds.Add(empId);
        }

        var dbEmployees = await _db.Employees
            .Where(e => employeeIds.Contains(e.EmployeeId))
            .Select(e => new { e.EmployeeId, e.Firstname, e.Lastname })
            .ToListAsync();

        foreach (var emp in dbEmployees)
        {
            var key = $"employee:{emp.EmployeeId}:last_location";
            var locationData = await _redis.GetAsync(key);
            if (string.IsNullOrEmpty(locationData)) continue;

            try
            {
                using var doc = JsonDocument.Parse(locationData.ToString());
                var root = doc.RootElement;

                decimal lat = 0, lng = 0;
                decimal? speed = null;
                bool isMoving = false;
                string status = "available";
                string? currentTask = null;
                string lastUpdated = "Just now";

                if (root.TryGetProperty("latitude", out var latProp)) lat = latProp.GetDecimal();
                if (root.TryGetProperty("longitude", out var lngProp)) lng = lngProp.GetDecimal();
                if (root.TryGetProperty("speed", out var speedProp)) speed = speedProp.GetDecimal();
                if (root.TryGetProperty("isMoving", out var movingProp)) isMoving = movingProp.GetBoolean();
                if (root.TryGetProperty("status", out var statusProp)) status = statusProp.GetString() ?? "available";
                if (root.TryGetProperty("updatedAt", out var updatedProp))
                    lastUpdated = FormatTimeAgo(updatedProp.GetDateTime());

                if (status == "on_service")
                {
                    var currentOrder = await _db.OrderServices
                        .Include(os => os.PackageService).ThenInclude(ps => ps.Service)
                        .Include(os => os.Order).ThenInclude(o => o.PickupLocation)
                        .Where(os => os.AssignedEmployeeId == emp.EmployeeId && os.ServiceStatus == ServiceStatus.InProgress)
                        .FirstOrDefaultAsync();

                    if (currentOrder != null)
                        currentTask = $"{currentOrder.PackageService?.Service?.ServiceName ?? "Service"} - {currentOrder.Order?.PickupLocation?.City ?? "Unknown"}";
                }

                drivers.Add(new LiveDriverItem
                {
                    EmployeeId = emp.EmployeeId,
                    Name = $"{emp.Firstname} {emp.Lastname}",
                    Code = $"EMP{emp.EmployeeId:D3}",
                    Latitude = lat,
                    Longitude = lng,
                    Status = status,
                    CurrentTask = currentTask,
                    SpeedKmh = speed,
                    IsMoving = isMoving,
                    LastUpdated = lastUpdated
                });
            }
            catch { /* ignore parsing errors */ }
        }

        return new LiveLocationsResponse
        {
            ActiveCount = drivers.Count,
            Drivers = drivers
        };
    }

    // ===== Helpers =====
    private static string FormatTimeAgo(DateTime updatedAt)
    {
        var diff = DateTime.UtcNow - updatedAt;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} minutes ago";
        return $"{(int)diff.TotalHours} hours ago";
    }

    private static (string Display, string Code) MapOrderStatus(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Pending => ("New", "pending"),
            OrderStatus.Confirmed => ("Confirmed", "confirmed"),
            OrderStatus.InProgress => ("On Going", "in_progress"),
            OrderStatus.Completed => ("Completed", "completed"),
            OrderStatus.Cancelled => ("Cancelled", "cancelled"),
            _ => (status.ToString(), status.ToString().ToLower())
        };
    }
}
