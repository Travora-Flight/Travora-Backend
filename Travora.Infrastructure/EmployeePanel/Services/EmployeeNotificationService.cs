using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Employee.Notifications;
using Travora.Application.Interfaces.Services.Employee;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.EmployeePanel.Services;

public class EmployeeNotificationService : IEmployeeNotificationService
{
    private readonly ApplicationDbContext _db;

    public EmployeeNotificationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<EmployeeNotificationsResponse> GetNotificationsAsync(int employeeId, int page, int pageSize)
    {
        var query = _db.Notifications
            .Where(n => n.UserId == employeeId && n.UserType == UserType.Employee)
            .OrderByDescending(n => n.SentAt);

        var unreadCount = await query.CountAsync(n => !n.IsRead);

        var notifications = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new EmployeeNotificationItemDto
            {
                NotificationId = n.NotificationId,
                Type = n.NotificationType.ToString(),
                Title = n.Title,
                Message = n.Message,
                OrderId = n.OrderId,
                IsRead = n.IsRead,
                SentAt = FormatTimeAgo(n.SentAt)
            })
            .ToListAsync();

        return new EmployeeNotificationsResponse
        {
            UnreadCount = unreadCount,
            Notifications = notifications
        };
    }

    public async Task MarkAsReadAsync(int employeeId, int notificationId)
    {
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == notificationId
                && n.UserId == employeeId
                && n.UserType == UserType.Employee)
            ?? throw new KeyNotFoundException("Notification not found");

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task MarkAllAsReadAsync(int employeeId)
    {
        var unread = await _db.Notifications
            .Where(n => n.UserId == employeeId
                && n.UserType == UserType.Employee
                && !n.IsRead)
            .ToListAsync();

        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    private static string FormatTimeAgo(DateTime dateTime)
    {
        var diff = DateTime.UtcNow - dateTime;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return dateTime.ToString("dd/MM/yyyy");
    }
}
