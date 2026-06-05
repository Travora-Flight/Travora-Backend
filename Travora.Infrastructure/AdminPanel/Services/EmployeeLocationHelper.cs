using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

/// <summary>
/// Shared helper for parsing employee location data from Redis
/// and fetching current task information from the database.
/// Used by AdminDashboardService and AdminLiveTrackerService.
/// </summary>
internal static class EmployeeLocationHelper
{
    /// <summary>
    /// Parsed employee location data from Redis.
    /// </summary>
    public record ParsedLocation
    {
        public decimal Latitude { get; init; }
        public decimal Longitude { get; init; }
        public decimal? Speed { get; init; }
        public bool IsMoving { get; init; }
        public decimal? Heading { get; init; }
        public string Status { get; init; } = "available";
        public string? LocationDescription { get; init; }
        public string LastUpdated { get; init; } = "Just now";
    }

    /// <summary>
    /// Parses employee location JSON stored in Redis.
    /// Returns null if locationData is empty or parsing fails.
    /// </summary>
    public static ParsedLocation? ParseRedisLocation(string? locationData)
    {
        if (string.IsNullOrEmpty(locationData))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(locationData);
            var root = doc.RootElement;

            decimal lat = 0, lng = 0;
            decimal? speed = null, heading = null;
            bool isMoving = false;
            string status = "available";
            string? locationDesc = null;
            string lastUpdated = "Just now";

            if (root.TryGetProperty("latitude", out var latProp)) lat = latProp.GetDecimal();
            if (root.TryGetProperty("longitude", out var lngProp)) lng = lngProp.GetDecimal();
            if (root.TryGetProperty("speed", out var speedProp)) speed = speedProp.GetDecimal();
            if (root.TryGetProperty("isMoving", out var movingProp)) isMoving = movingProp.GetBoolean();
            if (root.TryGetProperty("heading", out var headingProp)) heading = headingProp.GetDecimal();
            if (root.TryGetProperty("status", out var statusProp)) status = statusProp.GetString() ?? "available";
            if (root.TryGetProperty("location", out var locProp)) locationDesc = locProp.GetString();
            if (root.TryGetProperty("updatedAt", out var updatedProp))
                lastUpdated = FormatTimeAgo(updatedProp.GetDateTime());

            return new ParsedLocation
            {
                Latitude = lat,
                Longitude = lng,
                Speed = speed,
                IsMoving = isMoving,
                Heading = heading,
                Status = status,
                LocationDescription = locationDesc,
                LastUpdated = lastUpdated
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if an employee status indicates they are currently on a task.
    /// </summary>
    public static bool IsOnTask(string status) =>
        status is "on_service" or "on_duty";

    /// <summary>
    /// Gets the current task description for an employee.
    /// When detailed is true, returns full address format for detail views.
    /// </summary>
    public static async Task<string?> GetCurrentTaskAsync(
        ApplicationDbContext db, int employeeId, bool detailed = false)
    {
        var currentOrder = await db.OrderServices
            .Include(os => os.PackageService).ThenInclude(ps => ps.Service)
            .Include(os => os.Order).ThenInclude(o => o.PickupLocation)
            .Where(os => os.AssignedEmployeeId == employeeId
                         && os.ServiceStatus == ServiceStatus.InProgress)
            .FirstOrDefaultAsync();

        if (currentOrder == null) return null;

        if (detailed)
        {
            var pickup = currentOrder.Order?.PickupLocation;
            return $"Client pickup - {pickup?.StreetAddress ?? "Unknown"}, " +
                   $"{pickup?.City ?? "Unknown City"}, " +
                   $"{pickup?.Country ?? "Unknown Country"}";
        }

        return $"{currentOrder.PackageService?.Service?.ServiceName ?? "Service"} - " +
               $"{currentOrder.Order?.PickupLocation?.City ?? "Unknown"}";
    }

    /// <summary>
    /// Formats a DateTime as a human-readable "time ago" string.
    /// </summary>
    public static string FormatTimeAgo(DateTime updatedAt)
    {
        var diff = DateTime.UtcNow - updatedAt;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} minutes ago";
        return $"{(int)diff.TotalHours} hours ago";
    }

    /// <summary>
    /// Extracts employee IDs from Redis location keys.
    /// Keys are expected in format "employee:{id}:last_location".
    /// </summary>
    public static List<int> ExtractEmployeeIds(IEnumerable<string> keys)
    {
        var ids = new List<int>();
        foreach (var key in keys)
        {
            var parts = key.ToString().Split(':');
            if (parts.Length == 3 && int.TryParse(parts[1], out int empId))
                ids.Add(empId);
        }
        return ids;
    }
}
