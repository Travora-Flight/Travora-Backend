using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.Json;
using Travora.Application.DTOs.Admin.Dashboard;
using Travora.Application.Interfaces;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly ApplicationDbContext _db;
    private readonly IConnectionMultiplexer _redis;

    public AdminDashboardService(ApplicationDbContext db, IConnectionMultiplexer redis)
    {
        _db = db;
        _redis = redis;
    }

    public async Task<DashboardStatsResponse> GetDashboardStatsAsync()
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);

        // الأساسيات
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
        for (int i = 6; i >= 0; i--) // من الأقدم للأحدث خلال الأسبوع
        {
            var day = now.Date.AddDays(-i);
            var stats = ordersLast7Days.FirstOrDefault(o => o.Date == day);
            weeklyActivity.Add(new WeeklyActivityItem
            {
                Day = day.ToString("ddd"), // e.g. Mon, Tue
                Completed = stats?.Completed ?? 0,
                NewRequests = stats?.NewReqs ?? 0,
                Ongoing = stats?.Ongoing ?? 0
            });
        }

        // آخر 10 طلبات
        var lastOrders = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderServices)
                .ThenInclude(os => os.AssignedEmployee)
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .ToListAsync();

        var lastRequests = lastOrders.Select(o => new LastRequestItem
        {
            OrderId = o.Id,
            ClientName = o.Customer?.FullName ?? "Unknown",
            Type = o.IsPackage ? "package_service" : "service",
            Status = o.OrderStatus.ToString().ToLower(),
            AssignedEmployee = o.OrderServices.FirstOrDefault(os => os.AssignedEmployee != null)?.AssignedEmployee?.FullName ?? "Not Assigned",
            Time = o.CreatedAt.ToString("hh:mm tt")
        }).ToList();

        // موظفين الأونلاين (عبر Redis)
        var onlineEmployees = await GetOnlineEmployeesAsync();

        return new DashboardStatsResponse
        {
            AllEmployees = allEmployees,
            AllEmployeesGrowth = 0, // Mock: تحتاج مقارنة بالشهر الماضي إذا مطلوب
            NewRequests = newRequests,
            NewRequestsGrowth = 0,
            CurrentRequests = currentRequests,
            CurrentRequestsChange = 0,
            DoneRequests = doneRequests,
            DoneRequestsGrowth = 0,
            WeeklyActivity = weeklyActivity,
            OnlineEmployees = onlineEmployees,
            LastRequests = lastRequests
        };
    }

    private async Task<List<OnlineEmployeeItem>> GetOnlineEmployeesAsync()
    {
        var result = new List<OnlineEmployeeItem>();
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: "employee:*:last_location").ToList();

        if (!keys.Any()) return result;

        var db = _redis.GetDatabase();
        var activeEmployeeIds = new List<int>();

        foreach (var key in keys)
        {
            var parts = key.ToString().Split(':');
            if (parts.Length == 3 && int.TryParse(parts[1], out int empId))
            {
                activeEmployeeIds.Add(empId);
            }
        }

        var activeEmployeesFromDb = await _db.Employees
            .Where(e => activeEmployeeIds.Contains(e.EmployeeId))
            .ToListAsync();

        foreach (var emp in activeEmployeesFromDb)
        {
            result.Add(new OnlineEmployeeItem
            {
                EmployeeId = emp.EmployeeId,
                Name = $"{emp.Firstname} {emp.Lastname}",
                Location = "Active", // لاحقاً يمكن قراءة الموقع من بيانات Redis
                IsOnline = true
            });
        }

        return result;
    }
}
