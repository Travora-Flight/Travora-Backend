using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Travora.Application.DTOs.Flights;
using Travora.Application.Interfaces.External;
using Travora.Application.Interfaces.External.Weather;
using Travora.Application.Interfaces.Services;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.Services;

public class FlightPredictionService : IFlightPredictionService
{
    private readonly IAirlineService _airlineService;
    private readonly IWeatherService _weatherService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationDbContext _db;
    private readonly INotificationPusher _notificationPusher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlightPredictionService> _logger;

    public FlightPredictionService(
        IAirlineService airlineService,
        IWeatherService weatherService,
        IHttpClientFactory httpClientFactory,
        ApplicationDbContext db,
        INotificationPusher notificationPusher,
        IConfiguration configuration,
        ILogger<FlightPredictionService> logger)
    {
        _airlineService = airlineService;
        _weatherService = weatherService;
        _httpClientFactory = httpClientFactory;
        _db = db;
        _notificationPusher = notificationPusher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<DelayPredictionResponseDto?> PredictAndNotifyFlightDelayAsync(Flight flight)
    {
        var enabled = _configuration.GetValue<bool>("FlightDelayPrediction:Enabled");
        if (!enabled)
        {
            _logger.LogInformation("Flight delay prediction is disabled via configuration.");
            return null;
        }

        var mlModelUrl = _configuration["FlightDelayPrediction:MlModelUrl"];
        if (string.IsNullOrEmpty(mlModelUrl))
        {
            _logger.LogWarning("Flight delay prediction ML Model URL is not configured.");
            return null;
        }

        _logger.LogInformation("Starting flight delay prediction for flight {FlightNumber} (Departure: {Dep}, Scheduled: {Time})",
            flight.FlightNumber, flight.DepartureIataCode, flight.ScheduledDepartureTime);

        // 1. Fetch simulation traffic / congestion features
        var simulationFeatures = await _airlineService.GetDelayPredictionFeaturesAsync(
            flight.FlightNumber, 
            flight.DepartureIataCode, 
            flight.ScheduledDepartureTime
        );

        if (simulationFeatures == null)
        {
            _logger.LogWarning("Could not retrieve simulation features for flight {FlightNumber}.", flight.FlightNumber);
            return null;
        }

        // 2. Fetch hourly weather forecast
        var weather = await _weatherService.GetHourlyWeatherAsync(flight.DepartureIataCode, flight.ScheduledDepartureTime);
        if (weather == null)
        {
            _logger.LogWarning("Could not retrieve weather forecast for flight {FlightNumber} at {Time}. Using default fallback weather parameters to ensure prediction succeeds.", 
                flight.FlightNumber, flight.ScheduledDepartureTime);
            
            weather = new PredictionWeatherDto
            {
                TempF = 77.0,
                WindChillF = 77.0,
                Humidity = 50,
                WindspeedKmph = 12.0,
                WindGustKmph = 15.0,
                WinddirDegree = 180,
                WeatherCode = 1000,
                PrecipMM = 0.0,
                Visibility = 10.0,
                Pressure = 1013.0,
                Cloudcover = 20,
                DewPointF = 57.0
            };
        }

        // 3. Construct prediction payload
        var payload = new DelayPredictionRequestDto
        {
            Departure = new PredictionDepartureDto
            {
                ScheduledDeparture = flight.ScheduledDepartureTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                IataCode = flight.DepartureIataCode
            },
            Arrival = new PredictionArrivalDto
            {
                IataCode = flight.ArrivalIataCode
            },
            Weather = weather,
            OriginTotalTrafficHour = simulationFeatures.OriginTotalTrafficHour,
            OriginHistAvgCongestion = simulationFeatures.OriginHistAvgCongestion
        };

        // 4. Call Python ML API
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(mlModelUrl);

            var response = await client.PostAsJsonAsync("/predict", payload);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("ML Model prediction API call failed: {StatusCode} - {Body}", response.StatusCode, errorBody);
                return null;
            }

            var predictionResult = await response.Content.ReadFromJsonAsync<DelayPredictionResponseDto>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (predictionResult == null)
            {
                _logger.LogWarning("ML Model prediction API returned an empty response.");
                return null;
            }

            _logger.LogInformation("ML Prediction for flight {FlightNumber}: Delay = {DelayMinutes} minutes, Status = {Status}",
                flight.FlightNumber, predictionResult.PredictedDelayMinutes, predictionResult.Status);

            // 5. Trigger alerts if status indicates delay or predicted delay >= 15 mins
            if (predictionResult.Status.Equals("Delayed", StringComparison.OrdinalIgnoreCase) || predictionResult.PredictedDelayMinutes >= 15)
            {
                // Retrieve all customers and their order IDs for this flight
                var customerOrders = await _db.Orders
                    .Where(o => o.FlightId == flight.FlightId && o.OrderStatus != OrderStatus.Cancelled)
                    .Select(o => new { o.CustomerId, o.OrderId })
                    .ToListAsync();

                // Retrieve all guests who saved this flight
                var guestIds = await _db.SavedFlights
                    .Where(sf => sf.FlightId == flight.FlightId && sf.IsActive && !string.IsNullOrEmpty(sf.GuestId))
                    .Select(sf => sf.GuestId!)
                    .Distinct()
                    .ToListAsync();

                var title = "Flight Delay Warning ✈️";
                var message = $"Dear passenger, our systems predict a potential delay of {Math.Round(predictionResult.PredictedDelayMinutes)} minutes for your flight {flight.FlightNumber} departing from {flight.DepartureIataCode}.";

                // Send to Customers (each customer may have multiple orders on this flight)
                var notifiedCustomers = new HashSet<int>();
                foreach (var co in customerOrders)
                {
                    // 1. Live Push via SignalR (once per customer)
                    if (notifiedCustomers.Add(co.CustomerId))
                    {
                        await _notificationPusher.PushToCustomerAsync(
                            co.CustomerId,
                            title,
                            message,
                            "flight_delay_warning",
                            co.OrderId
                        );
                    }

                    // 2. Insert to database (one per order so each order is linked)
                    var notification = new Notification
                    {
                        UserId = co.CustomerId,
                        UserType = UserType.Customer,
                        NotificationType = NotificationType.SystemAlert,
                        Title = title,
                        Message = message,
                        NotificationChannel = NotificationChannel.Push,
                        SentAt = DateTime.UtcNow,
                        Priority = Priority.High,
                        OrderId = co.OrderId
                    };
                    _db.Notifications.Add(notification);
                }

                // Send to Guests
                foreach (var guestId in guestIds)
                {
                    await _notificationPusher.PushToGuestAsync(
                        guestId,
                        title,
                        message,
                        "flight_delay_warning",
                        null
                    );
                }

                await _db.SaveChangesAsync();
            }

            return predictionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing delay prediction for flight {FlightNumber}", flight.FlightNumber);
            return null;
        }
    }
}
