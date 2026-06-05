using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.LiveTracker;
using Travora.Application.Interfaces;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminLiveTrackerService : IAdminLiveTrackerService
{
    private readonly ApplicationDbContext _db;
    private readonly IUpstashRedisService _redis;

    public AdminLiveTrackerService(ApplicationDbContext db, IUpstashRedisService redis)
    {
        _db = db;
        _redis = redis;
    }

    public async Task<LiveEmployeeResponse> GetLastLocationsAsync(string? filter, string? search)
    {
        var drivers = await _db.Employees
            .Where(e => e.IsActive && e.JobRole == JobRole.Driver)
            .Select(e => new { e.EmployeeId, e.Firstname, e.Lastname, e.PhoneNumber })
            .ToListAsync();

        var response = new LiveEmployeeResponse();

        foreach (var driver in drivers)
        {
            var locationData = await _redis.GetAsync($"employee:{driver.EmployeeId}:last_location");
            var parsed = EmployeeLocationHelper.ParseRedisLocation(locationData);

            string status = "offline";
            decimal lat = 0, lng = 0;
            bool isOnline = false;
            string lastUpdated = "offline";
            string? locationDesc = "Unknown";
            string? currentTask = null;

            if (parsed != null)
            {
                isOnline = true;
                status = parsed.Status;
                lat = parsed.Latitude;
                lng = parsed.Longitude;
                locationDesc = parsed.LocationDescription ?? "Unknown";
                lastUpdated = parsed.LastUpdated;
            }

            if (EmployeeLocationHelper.IsOnTask(status))
                currentTask = await EmployeeLocationHelper.GetCurrentTaskAsync(_db, driver.EmployeeId);

            response.Employees.Add(new LiveEmployeeItem
            {
                EmployeeId = driver.EmployeeId,
                Name = $"{driver.Firstname} {driver.Lastname}",
                Code = $"EMP{driver.EmployeeId:D3}",
                JobRole = "driver",
                Status = status,
                CurrentTask = currentTask,
                Location = locationDesc,
                Latitude = lat,
                Longitude = lng,
                IsOnline = isOnline,
                LastUpdated = lastUpdated,
                Mobile = driver.PhoneNumber
            });
        }

        // Apply filters
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            response.Employees = response.Employees.Where(e => e.Name.ToLower().Contains(s) || e.Code.ToLower().Contains(s)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter) && filter != "all")
        {
            response.Employees = response.Employees.Where(e => e.Status == filter).ToList();
        }

        response.Available = response.Employees.Count(e => e.Status == "available");
        response.OnService = response.Employees.Count(e => EmployeeLocationHelper.IsOnTask(e.Status));

        return response;
    }

    public async Task<EmployeeLocationDetailResponse> GetEmployeeLocationDetailsAsync(int employeeId)
    {
        var driver = await _db.Employees
            .Where(e => e.EmployeeId == employeeId && e.JobRole == JobRole.Driver)
            .Select(e => new { e.EmployeeId, e.Firstname, e.Lastname })
            .FirstOrDefaultAsync() ?? throw new KeyNotFoundException("Driver not found");

        var locationData = await _redis.GetAsync($"employee:{driver.EmployeeId}:last_location");
        var parsed = EmployeeLocationHelper.ParseRedisLocation(locationData);

        string status = "offline";
        decimal lat = 0, lng = 0;
        decimal? speedKmh = null, heading = null;
        bool isMoving = false;
        string lastUpdated = "offline";
        string? currentTask = null;

        if (parsed != null)
        {
            status = parsed.Status;
            lat = parsed.Latitude;
            lng = parsed.Longitude;
            speedKmh = parsed.Speed;
            isMoving = parsed.IsMoving;
            heading = parsed.Heading;
            lastUpdated = parsed.LastUpdated;
        }

        if (EmployeeLocationHelper.IsOnTask(status))
            currentTask = await EmployeeLocationHelper.GetCurrentTaskAsync(_db, driver.EmployeeId, detailed: true);

        return new EmployeeLocationDetailResponse
        {
            EmployeeId = driver.EmployeeId,
            Name = $"{driver.Firstname} {driver.Lastname}",
            Code = $"EMP{driver.EmployeeId:D3}",
            Status = status,
            CurrentTask = currentTask,
            Latitude = lat,
            Longitude = lng,
            SpeedKmh = speedKmh,
            IsMoving = isMoving,
            Heading = heading,
            LastUpdated = lastUpdated
        };
    }
}
