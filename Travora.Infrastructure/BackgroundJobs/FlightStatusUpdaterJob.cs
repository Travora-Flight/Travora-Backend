using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Travora.Application.Interfaces.Services;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.BackgroundJobs;

public class FlightStatusUpdaterJob : IFlightStatusUpdaterJob
{
    private readonly ApplicationDbContext _db;
    private readonly IFlightTrackerService _trackerService;
    private readonly INotificationPusher _notificationPusher;
    private readonly ILogger<FlightStatusUpdaterJob> _logger;

    public FlightStatusUpdaterJob(
        ApplicationDbContext db, 
        IFlightTrackerService trackerService, 
        INotificationPusher notificationPusher,
        ILogger<FlightStatusUpdaterJob> logger)
    {
        _db = db;
        _trackerService = trackerService;
        _notificationPusher = notificationPusher;
        _logger = logger;
    }

    public async Task UpdateFlightStatusesAsync()
    {
        _logger.LogInformation("Starting Flight Status Updater background job...");

        // 1. Resource optimization: If no active tracked flights exist, do 0 API calls.
        var activeSavedFlightsCount = await _db.SavedFlights.CountAsync(sf => sf.IsActive);
        if (activeSavedFlightsCount == 0)
        {
            _logger.LogInformation("No active saved flights found to track. Skipping API calls.");
            return;
        }

        // 2. Fetch distinct active flights that are currently tracked and not landed or cancelled
        var activeFlightsToUpdate = await _db.SavedFlights
            .Include(sf => sf.Flight)
            .Where(sf => sf.IsActive && sf.Flight != null && 
                         sf.Flight.FlightStatus != FlightStatus.Landed && 
                         sf.Flight.FlightStatus != FlightStatus.Cancelled)
            .Select(sf => sf.Flight!)
            .Distinct()
            .ToListAsync();

        if (!activeFlightsToUpdate.Any())
        {
            _logger.LogInformation("No active flights require updating (all flights are either Landed or Cancelled).");
            return;
        }

        _logger.LogInformation("Found {Count} active flights to update status.", activeFlightsToUpdate.Count);

        foreach (var flight in activeFlightsToUpdate)
        {
            try
            {
                var details = await _trackerService.GetFlightDetailsAsync(flight.FlightIataNumber);
                if (details == null)
                {
                    continue;
                }

                // Determine the new status
                var newStatus = FlightStatus.InAir;
                var statusStr = details.Status.ToLowerInvariant();
                if (statusStr.Contains("landed") || statusStr.Contains("arrived"))
                    newStatus = FlightStatus.Landed;
                else if (statusStr.Contains("scheduled"))
                    newStatus = FlightStatus.Scheduled;
                else if (statusStr.Contains("cancelled"))
                    newStatus = FlightStatus.Cancelled;
                else if (statusStr.Contains("delayed"))
                    newStatus = FlightStatus.Delayed;
                else if (statusStr.Contains("boarding"))
                    newStatus = FlightStatus.Boarding;
                else if (statusStr.Contains("departed"))
                    newStatus = FlightStatus.Departed;

                // If status has changed, update database and trigger notifications
                if (flight.FlightStatus != newStatus)
                {
                    var oldStatus = flight.FlightStatus;
                    flight.FlightStatus = newStatus;
                    
                    // Update metadata if available
                    if (details.Aircraft != null)
                    {
                        if (!string.IsNullOrEmpty(details.Aircraft.Registration))
                            flight.AircraftRegistrationNumber = details.Aircraft.Registration;
                        if (!string.IsNullOrEmpty(details.Aircraft.Model))
                            flight.AircraftModelText = details.Aircraft.Model;
                        if (!string.IsNullOrEmpty(details.Aircraft.Type))
                            flight.AircraftModelCode = details.Aircraft.Type;
                    }

                    await _db.SaveChangesAsync();

                    // Retrieve active notification-enabled saved trackers for this flight
                    var trackers = await _db.SavedFlights
                        .Where(sf => sf.FlightId == flight.FlightId && sf.IsActive && sf.NotificationEnabled)
                        .ToListAsync();

                    var title = $"Flight {flight.FlightNumber} Update";
                    var message = $"Your tracked flight {flight.FlightNumber} status changed from {oldStatus} to {newStatus}.";

                    foreach (var tracker in trackers)
                    {
                        if (tracker.CustomerId.HasValue)
                        {
                            // 1. Push notification via SignalR
                            await _notificationPusher.PushToCustomerAsync(
                                tracker.CustomerId.Value, 
                                title, 
                                message, 
                                "flight_update", 
                                null
                            );

                            // 2. Save in-app notification in DB
                            var notification = new Notification
                            {
                                UserId = tracker.CustomerId.Value,
                                UserType = UserType.Customer,
                                NotificationType = NotificationType.SystemAlert,
                                Title = title,
                                Message = message,
                                NotificationChannel = NotificationChannel.Push,
                                SentAt = DateTime.UtcNow,
                                Priority = Priority.Medium
                            };
                            _db.Notifications.Add(notification);
                        }
                        else if (!string.IsNullOrEmpty(tracker.GuestId))
                        {
                            // Push real-time alert to guest SignalR group
                            await _notificationPusher.PushToGuestAsync(
                                tracker.GuestId, 
                                title, 
                                message, 
                                "flight_update", 
                                null
                            );
                        }
                    }

                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while updating status for flight {FlightNumber}", flight.FlightNumber);
            }
        }
    }
}
