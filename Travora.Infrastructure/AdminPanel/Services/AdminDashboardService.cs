using Microsoft.EntityFrameworkCore;
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

        var allEmployees = await _db.Employees.CountAsync(e => e.IsActive && !e.IsDeleted);
        var newRequests = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Confirmed);
        var currentRequests = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.InProgress);
        var doneRequests = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Completed);

        // Activity last 7 days
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

            // Default demo values (remove when live data is available)
            var defaultCompleted = new[] { 5, 8, 3, 12, 7, 10, 6 }[6 - i];
            var defaultNew = new[] { 4, 6, 9, 5, 11, 8, 7 }[6 - i];
            var defaultOngoing = new[] { 2, 3, 4, 2, 5, 3, 4 }[6 - i];

            weeklyActivity.Add(new WeeklyActivityItem
            {
                Day = day.ToString("ddd"),
                Completed = stats?.Completed > 0 ? stats.Completed : defaultCompleted,
                NewRequests = stats?.NewReqs > 0 ? stats.NewReqs : defaultNew,
                Ongoing = stats?.Ongoing > 0 ? stats.Ongoing : defaultOngoing
            });
        }

        return new DashboardStatsResponse
        {
            AllEmployees = allEmployees,
            NewRequests = newRequests,
            CurrentRequests = currentRequests,
            DoneRequests = doneRequests,
            WeeklyActivity = weeklyActivity
        };
    }

    // ===== Online Employees =====
    public async Task<OnlineEmployeesResponse> GetOnlineEmployeesAsync()
    {
        var locations = await FetchOnlineEmployeeLocationsAsync();

        var employees = locations.Select(e => new OnlineEmployeeDetail
        {
            EmployeeId = e.EmployeeId,
            Name = e.Name,
            Code = e.Code,
            ProfileImageUrl = e.ProfileImagePath,
            Latitude = e.Location.Latitude,
            Longitude = e.Location.Longitude,
            Status = e.Location.Status,
            CurrentTask = e.CurrentTask,
            LastUpdated = e.Location.LastUpdated
        })
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
        var locations = await FetchOnlineEmployeeLocationsAsync();

        var drivers = locations.Select(e => new LiveDriverItem
        {
            EmployeeId = e.EmployeeId,
            Name = e.Name,
            Code = e.Code,
            Latitude = e.Location.Latitude,
            Longitude = e.Location.Longitude,
            Status = e.Location.Status,
            CurrentTask = e.CurrentTask,
            SpeedKmh = e.Location.Speed,
            IsMoving = e.Location.IsMoving,
            LastUpdated = e.Location.LastUpdated
        }).ToList();

        return new LiveLocationsResponse
        {
            ActiveCount = drivers.Count,
            Drivers = drivers
        };
    }

    // ===== Private Helpers =====

    /// <summary>
    /// Shared method: fetches all online employee locations from Redis,
    /// enriches with DB data and current task info.
    /// Used by both GetOnlineEmployeesAsync and GetLiveLocationsAsync.
    /// </summary>
    private record EmployeeWithLocation(
        int EmployeeId, string Name, string Code, string? ProfileImagePath,
        EmployeeLocationHelper.ParsedLocation Location, string? CurrentTask);

    private async Task<List<EmployeeWithLocation>> FetchOnlineEmployeeLocationsAsync()
    {
        var keys = await _redis.KeysAsync("employee:*:last_location");
        if (!keys.Any()) return new();

        var employeeIds = EmployeeLocationHelper.ExtractEmployeeIds(keys);

        var dbEmployees = await _db.Employees
            .Where(e => employeeIds.Contains(e.EmployeeId))
            .Select(e => new { e.EmployeeId, e.Firstname, e.Lastname, e.ProfileImagePath })
            .ToListAsync();

        var results = new List<EmployeeWithLocation>();

        foreach (var emp in dbEmployees)
        {
            var locationData = await _redis.GetAsync($"employee:{emp.EmployeeId}:last_location");
            var parsed = EmployeeLocationHelper.ParseRedisLocation(locationData);
            if (parsed == null) continue;

            string? currentTask = null;
            if (EmployeeLocationHelper.IsOnTask(parsed.Status))
                currentTask = await EmployeeLocationHelper.GetCurrentTaskAsync(_db, emp.EmployeeId);

            results.Add(new EmployeeWithLocation(
                emp.EmployeeId,
                $"{emp.Firstname} {emp.Lastname}",
                $"EMP{emp.EmployeeId:D3}",
                emp.ProfileImagePath,
                parsed,
                currentTask));
        }

        return results;
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
