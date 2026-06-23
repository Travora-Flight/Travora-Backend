using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Travora.Application.Interfaces.Services;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.BackgroundJobs;

public class FlightDelayPredictionJob : IFlightDelayPredictionJob
{
    private readonly ApplicationDbContext _db;
    private readonly IFlightPredictionService _predictionService;
    private readonly ILogger<FlightDelayPredictionJob> _logger;

    public FlightDelayPredictionJob(
        ApplicationDbContext db,
        IFlightPredictionService predictionService,
        ILogger<FlightDelayPredictionJob> logger)
    {
        _db = db;
        _predictionService = predictionService;
        _logger = logger;
    }

    public async Task PredictUpcomingFlightDelaysAsync()
    {
        _logger.LogInformation("Starting Flight Delay Prediction background job...");

        var now = DateTime.UtcNow;
        var upcomingThreshold = now.AddHours(2);

        // Fetch flights associated with active orders departing in the next 2 hours
        var orderFlights = await _db.Orders
            .Include(o => o.Flight)
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && 
                        o.OrderStatus != OrderStatus.Completed &&
                        o.Flight.FlightStatus == FlightStatus.Scheduled &&
                        o.Flight.ScheduledDepartureTime >= now &&
                        o.Flight.ScheduledDepartureTime <= upcomingThreshold)
            .Select(o => o.Flight)
            .ToListAsync();

        // Fetch active tracked saved flights departing in the next 2 hours
        var savedFlights = await _db.SavedFlights
            .Include(sf => sf.Flight)
            .Where(sf => sf.IsActive && sf.Flight != null &&
                        sf.Flight.FlightStatus == FlightStatus.Scheduled &&
                        sf.Flight.ScheduledDepartureTime >= now &&
                        sf.Flight.ScheduledDepartureTime <= upcomingThreshold)
            .Select(sf => sf.Flight!)
            .ToListAsync();

        // Combine and distinct
        var flightsToPredict = orderFlights
            .Concat(savedFlights)
            .GroupBy(f => f.FlightId)
            .Select(g => g.First())
            .ToList();

        if (!flightsToPredict.Any())
        {
            _logger.LogInformation("No upcoming scheduled flights departing in the next 2 hours to predict.");
            return;
        }

        _logger.LogInformation("Found {Count} upcoming flights (departing in next 2 hours) to check for delay predictions.", flightsToPredict.Count);

        foreach (var flight in flightsToPredict)
        {
            try
            {
                await _predictionService.PredictAndNotifyFlightDelayAsync(flight);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error predicting delay for flight {FlightNumber} in background job.", flight.FlightNumber);
            }
        }
    }
}
