using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Travora.Application.DTOs.External.Airline;
using Travora.Application.DTOs.Orders;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.Interfaces.External;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Customer;
using Travora.Domain.Constants;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.CustomerPanel.Services;

public class CustomerOrderService : ICustomerOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IAirlineService _airlineService;
    private readonly ICloudinaryService _cloudinaryService;
    private readonly INotificationPusher _notificationPusher;
    private readonly ILogger<CustomerOrderService> _logger;
    private readonly IRefundService _refundService;

    public CustomerOrderService(
        ApplicationDbContext context,
        IAirlineService airlineService,
        ICloudinaryService cloudinaryService,
        INotificationPusher notificationPusher,
        ILogger<CustomerOrderService> logger,
        IRefundService refundService)
    {
        _context = context;
        _airlineService = airlineService;
        _cloudinaryService = cloudinaryService;
        _notificationPusher = notificationPusher;
        _logger = logger;
        _refundService = refundService;
    }

    // ===================================================================
    // TRACKING STEP DEFINITIONS PER PACKAGE
    // ===================================================================
    private static readonly Dictionary<string, List<(string Step, BaggageTrackingStatus DbStatus, string Source, string Description)>> TrackingStepsByPackage = new()
    {
        // Door To Door: Check-in → Arrived at Airport → Security → Terminal → Gate → Loaded → Arrived → Customs → Out for Delivery → Delivered
        [PackageNames.DoorToDoor] = new()
        {
            ("Order Confirmed",      BaggageTrackingStatus.Registered,       "db",      "Order confirmed and service scheduled"),
            ("Check-in",             BaggageTrackingStatus.PickedUp,         "db",      "Bags picked up and checked-in by our driver"),
            ("Arrived at Airport",   BaggageTrackingStatus.ArrivedAtAirport, "db",      "Bags arrived at departure airport"),
            ("Security Check",       BaggageTrackingStatus.AtSecurity,       "airline", "Bags passed security check"),
            ("Terminal",             BaggageTrackingStatus.AtTerminal,       "airline", "Bags at airport terminal"),
            ("Gate",                 BaggageTrackingStatus.AtGate,           "airline", "Bags at boarding gate"),
            ("Loaded on Aircraft",   BaggageTrackingStatus.LoadedOnAircraft, "airline", "Bags loaded on the aircraft"),
            ("Arrived at Dest",      BaggageTrackingStatus.Arrived,          "airline", "Aircraft arrived at destination"),
            ("Customs Cleared",      BaggageTrackingStatus.AtCustoms,        "db",      "Bags cleared customs at destination"),
            ("Out for Delivery",     BaggageTrackingStatus.OutForDelivery,   "db",      "Bags out for delivery to your address"),
            ("Delivered",            BaggageTrackingStatus.Delivered,        "db",      "Delivered successfully")
        },
        // Car Service To Airport: Check-in → Arrived at Airport → Security → Terminal → Gate → Loaded on Aircraft
        [PackageNames.CarServiceToAirport] = new()
        {
            ("Order Confirmed",      BaggageTrackingStatus.Registered,       "db",      "Order confirmed"),
            ("Check-in",             BaggageTrackingStatus.PickedUp,         "db",      "Bags picked up and checked-in by our driver"),
            ("Arrived at Airport",   BaggageTrackingStatus.ArrivedAtAirport, "db",      "Bags arrived at departure airport"),
            ("Security Check",       BaggageTrackingStatus.AtSecurity,       "airline", "Bags passed security check"),
            ("Terminal",             BaggageTrackingStatus.AtTerminal,       "airline", "Bags at airport terminal"),
            ("Gate",                 BaggageTrackingStatus.AtGate,           "airline", "Bags at boarding gate"),
            ("Loaded on Aircraft",   BaggageTrackingStatus.LoadedOnAircraft, "airline", "Bags loaded on the aircraft")
        },
        // Car Service From Airport: Arrived at Dest → Customs (airline) → Out for Delivery → Delivered
        [PackageNames.CarServiceFromAirport] = new()
        {
            ("Order Confirmed",      BaggageTrackingStatus.Registered,       "db",      "Order confirmed"),
            ("Arrived at Dest",      BaggageTrackingStatus.Arrived,          "airline", "Aircraft arrived at destination"),
            ("Customs",              BaggageTrackingStatus.AtCustoms,        "airline", "Bags at destination customs"),
            ("Out for Delivery",     BaggageTrackingStatus.OutForDelivery,   "db",      "Bags out for delivery to your address"),
            ("Delivered",            BaggageTrackingStatus.Delivered,        "db",      "Delivered successfully")
        },
        // Bag Tracking: Security → Terminal → Gate → Loaded → Arrived → Baggage Belt (+ optional At Baggage Office)
        [PackageNames.TrackingBaggage] = new()
        {
            ("Bags Registered",      BaggageTrackingStatus.Registered,       "db",      "Bags registered for tracking"),
            ("Security Check",       BaggageTrackingStatus.AtSecurity,       "airline", "Bags passed security check"),
            ("Terminal",             BaggageTrackingStatus.AtTerminal,       "airline", "Bags at terminal"),
            ("Gate",                 BaggageTrackingStatus.AtGate,           "airline", "Bags at the gate"),
            ("Loaded on Aircraft",   BaggageTrackingStatus.LoadedOnAircraft, "airline", "Bags loaded on the aircraft"),
            ("Arrived at Dest",      BaggageTrackingStatus.Arrived,          "airline", "Aircraft arrived at destination"),
            ("Baggage Belt",         BaggageTrackingStatus.OnBelt,           "airline", "Bags ready for pickup from baggage belt")
        }
    };

    // Maps the airline API 'currentLocation' values to our tracking status
    private static readonly Dictionary<string, BaggageTrackingStatus> LocationToStatusMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Check-In"]           = BaggageTrackingStatus.PickedUp,
        ["Security Check"]     = BaggageTrackingStatus.AtSecurity,
        ["Customs"]            = BaggageTrackingStatus.AtCustoms,
        ["Terminal"]           = BaggageTrackingStatus.AtTerminal,
        ["Gate"]               = BaggageTrackingStatus.AtGate,
        ["Loaded on Aircraft"] = BaggageTrackingStatus.LoadedOnAircraft,
        ["Arrived at Dest"]    = BaggageTrackingStatus.Arrived,
        ["Arrived"]            = BaggageTrackingStatus.Arrived,
        ["Baggage Belt"]       = BaggageTrackingStatus.OnBelt
    };

    // ===================================================================
    // ENDPOINT 0 — List Orders
    // ===================================================================
    public async Task<IEnumerable<OrderListDto>> GetCustomerOrdersAsync(int customerId, OrderStatus? status = null, PackageFilter? package = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight)
            .Include(o => o.PickupLocation)
            .Include(o => o.DeliveryLocation)
            .Where(o => o.CustomerId == customerId);

        if (status.HasValue)
        {
            query = query.Where(o => o.OrderStatus == status.Value);
        }

        if (package.HasValue)
        {
            var code = package.Value switch
            {
                PackageFilter.DoorToDoor => PackageCodes.DoorToDoor,
                PackageFilter.CarServiceToAirport => PackageCodes.CarServiceToAirport,
                PackageFilter.CarServiceFromAirport => PackageCodes.CarServiceFromAirport,
                PackageFilter.TrackingBaggage => PackageCodes.TrackingBaggage,
                _ => string.Empty
            };

            query = query.Where(o => o.Package.PackageCode == code);
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var result = new List<OrderListDto>();
        foreach (var order in orders)
        {
            var packageName = order.Package?.PackageName ?? string.Empty;
            var packageCode = order.Package?.PackageCode ?? string.Empty;
            string? from = null, to = null;

            if (packageCode != PackageCodes.TrackingBaggage && packageName != PackageNames.TrackingBaggage)
            {
                switch (packageName)
                {
                    case PackageNames.DoorToDoor:
                        from = order.PickupLocation?.City;
                        to = order.DeliveryLocation?.City;
                        break;
                    case PackageNames.CarServiceToAirport:
                        from = order.PickupLocation?.City;
                        to = order.Flight?.ArrivalIataCode;
                        break;
                    case PackageNames.CarServiceFromAirport:
                        from = order.Flight?.DepartureIataCode;
                        to = order.DeliveryLocation?.City;
                        break;
                }
            }

            result.Add(new OrderListDto
            {
                OrderId = order.OrderId,
                PackageName = packageName,
                OrderStatus = order.OrderStatus.ToString(),
                CreatedAt = order.CreatedAt,
                TotalAmount = order.TotalAmount,
                From = from,
                To = to
            });
        }

        return result;
    }

    // ===================================================================
    // ENDPOINT 1 — Order Details
    // ===================================================================
    public async Task<OrderDetailsResponse> GetOrderDetailsAsync(int customerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight)
            .Include(o => o.PickupLocation)
            .Include(o => o.DeliveryLocation)
            .Include(o => o.OrderCompanions).ThenInclude(oc => oc.Companion)
            .Include(o => o.Baggages).ThenInclude(b => b.BaggageTrackings)
            .Include(o => o.OrderServices)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null)
            throw new KeyNotFoundException("Order not found");

        if (order.CustomerId != customerId)
            throw new UnauthorizedAccessException("You do not have permission to view this order");

        var packageName = order.Package?.PackageName ?? string.Empty;

        // --- From/To ---
        string? from = null, to = null;
        switch (packageName)
        {
            case PackageNames.DoorToDoor:
                from = order.PickupLocation?.City;
                to = order.DeliveryLocation?.City;
                break;
            case PackageNames.CarServiceToAirport:
                from = order.PickupLocation?.City;
                to = order.Flight?.ArrivalIataCode;
                break;
            case PackageNames.CarServiceFromAirport:
                from = order.Flight?.DepartureIataCode;
                to = order.DeliveryLocation?.City;
                break;
            case PackageNames.TrackingBaggage:
                from = order.Flight?.DepartureIataCode;
                to = order.Flight?.ArrivalIataCode;
                break;
        }

        if (order.OrderStatus == OrderStatus.Pending)
        {
            var totalWt = order.Baggages.Sum(b => b.TotalWeight ?? 0m);

            return new OrderDetailsResponse
            {
                OrderId = order.OrderId,
                PackageName = packageName,
                Status = "Awaiting Payment",
                From = from,
                To = to,
                NumberOfBags = order.TotalBaggageCount,
                TotalWeight = totalWt,
                NumberOfPassengers = order.OrderCompanions.Count + 1,
                CanCancel = true,
                HasBoardingPass = packageName is PackageNames.DoorToDoor or PackageNames.CarServiceToAirport,
                Appointment = null,
                TrackingStatus = new List<TrackingStepDto>(),
                TrackingMessage = "Tracking is not available until payment is completed"
            };
        }

        // ===================================================================
        // TRACKING — Persist airline updates & build per-step timestamps
        // ===================================================================

        // 1) Collect all existing BaggageTracking records per bag (keyed by status)
        var allTrackingRecords = order.Baggages
            .SelectMany(b => b.BaggageTrackings)
            .ToList();

        // 2) Query Airline API for current status per bag
        var airlineBagMap = new Dictionary<string, (BaggageTrackingStatus Status, DateTime? UpdatedAt)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var ticketNumbers = new List<string>();
            if (!string.IsNullOrEmpty(order.TicketNumber))
                ticketNumbers.Add(order.TicketNumber);
            foreach (var oc in order.OrderCompanions)
                if (!string.IsNullOrEmpty(oc.TicketNumber))
                    ticketNumbers.Add(oc.TicketNumber);

            var airlineResults = await Task.WhenAll(
                ticketNumbers.Select(tn => _airlineService.GetBaggageByTicketAsync(tn, cancellationToken)));

            foreach (var result in airlineResults)
            {
                foreach (var bag in result.Bags)
                {
                    if (LocationToStatusMap.TryGetValue(bag.CurrentLocation, out var mappedStatus))
                        airlineBagMap[bag.TagNumber] = (mappedStatus, bag.LastLocationUpdatedAt);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Airline API failed for order {OrderId}, using DB tracking only", orderId);
        }

        // 3) Persist NEW airline statuses to BaggageTracking (only if not already recorded)
        var newTrackingRecords = new List<Domain.Entities.BaggageTracking>();
        foreach (var bag in order.Baggages)
        {
            if (string.IsNullOrEmpty(bag.BaggageNumber)) continue;
            if (!airlineBagMap.TryGetValue(bag.BaggageNumber, out var airlineData)) continue;

            var existingStatuses = allTrackingRecords
                .Where(t => t.BaggageId == bag.BaggageId)
                .Select(t => t.Status)
                .ToHashSet();

            // Only persist the CURRENT airline status (the one actually reported)
            if (!existingStatuses.Contains(airlineData.Status))
            {
                var record = new Domain.Entities.BaggageTracking
                {
                    BaggageId = bag.BaggageId,
                    Status = airlineData.Status,
                    ArrivalTime = airlineData.UpdatedAt ?? DateTime.UtcNow,
                    HandledByEmployeeId = null, // Airline-sourced
                    GpsLatitude = 0,
                    GpsLongitude = 0
                };
                newTrackingRecords.Add(record);
            }
        }

        if (newTrackingRecords.Count > 0)
        {
            _context.BaggageTrackings.AddRange(newTrackingRecords);
            await _context.SaveChangesAsync(cancellationToken);

            // Merge into our working set
            allTrackingRecords.AddRange(newTrackingRecords);
        }

        // 4) Determine overall status: per bag MAX(all statuses), then MIN across bags
        var overallStatus = BaggageTrackingStatus.Cancelled;
        var hasBags = order.Baggages.Any();

        foreach (var bag in order.Baggages)
        {
            var bagHighest = allTrackingRecords
                .Where(t => t.BaggageId == bag.BaggageId)
                .Select(t => (int)t.Status)
                .DefaultIfEmpty((int)BaggageTrackingStatus.Registered)
                .Max();

            if (bagHighest < (int)overallStatus)
                overallStatus = (BaggageTrackingStatus)bagHighest;
        }

        if (!hasBags || overallStatus == BaggageTrackingStatus.Cancelled)
            overallStatus = BaggageTrackingStatus.Registered;

        // 5) Build timestamp lookup: for each status, take the LATEST ArrivalTime across all bags
        //    (the time the last bag reached that status = when ALL bags reached it)
        var timestampByStatus = allTrackingRecords
            .GroupBy(t => t.Status)
            .ToDictionary(g => g.Key, g => g.Max(t => t.ArrivalTime));

        // 6) Auto-Complete logic for Bag Tracking
        if (packageName == PackageNames.TrackingBaggage && 
            overallStatus >= BaggageTrackingStatus.OnBelt && 
            order.OrderStatus != OrderStatus.Completed)
        {
            var orderToUpdate = await _context.Orders
                .Include(o => o.OrderServices)
                .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

            if (orderToUpdate != null && orderToUpdate.OrderStatus != OrderStatus.Completed)
            {
                orderToUpdate.OrderStatus = OrderStatus.Completed;
                orderToUpdate.UpdatedAt = DateTime.UtcNow;

                var svc = orderToUpdate.OrderServices.FirstOrDefault();
                if (svc != null)
                {
                    svc.ServiceStatus = ServiceStatus.Completed;
                    svc.ActualEndTime = DateTime.UtcNow;
                    svc.UpdatedAt = DateTime.UtcNow;
                }

                _context.Notifications.Add(new Domain.Entities.Notification
                {
                    UserId = order.CustomerId,
                    UserType = UserType.Customer,
                    NotificationType = NotificationType.OrderCompleted,
                    Title = "Your bag has arrived",
                    Message = "Your bag is ready for pickup from the baggage belt",
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = orderId
                });

                await _context.SaveChangesAsync(cancellationToken);

                await _notificationPusher.PushToCustomerAsync(
                    order.CustomerId,
                    "Your bag has arrived",
                    "Your bag is ready for pickup from the baggage belt",
                    "OrderCompleted",
                    orderId);
                
                order.OrderStatus = OrderStatus.Completed;
            }
        }

        // 7) Build tracking steps with real per-step timestamps
        var trackingSteps = new List<TrackingStepDto>();
        if (TrackingStepsByPackage.TryGetValue(packageName, out var steps))
        {
            foreach (var (stepName, stepStatus, source, description) in steps)
            {
                bool isDone = (int)overallStatus >= (int)stepStatus;

                // Get timestamp from the actual DB record for this specific status
                DateTime? timestamp = isDone && timestampByStatus.TryGetValue(stepStatus, out var ts)
                    ? ts
                    : null;

                string stepDescription = stepName == "Bags Registered" && packageName == PackageNames.TrackingBaggage
                    ? $"{order.TotalBaggageCount} bags registered in the system"
                    : description;

                trackingSteps.Add(new TrackingStepDto
                {
                    Step = stepName,
                    Timestamp = timestamp,
                    IsDone = isDone,
                    Description = stepDescription
                });
            }
        }

        // Registered step: use order creation time as fallback
        var registeredStep = trackingSteps.FirstOrDefault();
        if (registeredStep != null && registeredStep.IsDone && registeredStep.Timestamp == null)
        {
            registeredStep.Timestamp = order.CreatedAt;
        }

        // --- Status label ---
        string statusLabel = overallStatus switch
        {
            BaggageTrackingStatus.Registered => "Order Confirmed",
            BaggageTrackingStatus.PickedUp => "Picked Up",
            BaggageTrackingStatus.ArrivedAtAirport => "At Airport",
            BaggageTrackingStatus.AtSecurity => "Security Check",
            BaggageTrackingStatus.AtTerminal => "At Terminal",
            BaggageTrackingStatus.AtGate => "At Gate",
            BaggageTrackingStatus.LoadedOnAircraft => "Loaded on Aircraft",
            BaggageTrackingStatus.Arrived => "Arrived",
            BaggageTrackingStatus.AtCustoms => "At Customs",
            BaggageTrackingStatus.OnBelt => "Baggage Belt",
            BaggageTrackingStatus.AtBaggageOffice => "At Baggage Office",
            BaggageTrackingStatus.OutForDelivery => "Out for Delivery",
            BaggageTrackingStatus.Delivered => "Delivered",
            _ => order.OrderStatus.ToString()
        };

        // --- Weight ---
        decimal totalWeight = order.Baggages.Sum(b => b.TotalWeight ?? 0m);

        // --- canCancel ---
        bool hasServiceInProgress = order.OrderServices
            .Any(os => os.ServiceStatus == ServiceStatus.InProgress);

        bool canCancel = order.OrderStatus is not (OrderStatus.Completed or OrderStatus.Cancelled)
                         && !hasServiceInProgress;

        // --- hasBoardingPass ---
        bool hasBoardingPass = packageName is PackageNames.DoorToDoor or PackageNames.CarServiceToAirport;

        // --- Appointment ---
        AppointmentDto? appointment = null;
        if (packageName != PackageNames.TrackingBaggage)
        {
            if (packageName == PackageNames.CarServiceFromAirport)
            {
                appointment = new AppointmentDto
                {
                    Delivery = new AppointmentSlot
                    {
                        Date = order.DeliveryDate.ToString("dddd, MMMM dd, yyyy"),
                        Time = FormatSlotTime(order.DeliveryTimeSlot)
                    }
                };
            }
            else
            {
                appointment = new AppointmentDto
                {
                    Pickup = new AppointmentSlot
                    {
                        Date = order.PickupDate.ToString("dddd, MMMM dd, yyyy"),
                        Time = FormatSlotTime(order.PickupTimeSlot)
                    }
                };

                if (packageName == PackageNames.DoorToDoor)
                {
                    appointment.Delivery = new AppointmentSlot
                    {
                        Date = order.DeliveryDate.ToString("dddd, MMMM dd, yyyy"),
                        Time = FormatSlotTime(order.DeliveryTimeSlot)
                    };
                }
            }
        }

        // --- Tracking Message ---
        string? trackingMessage = null;
        bool allStepsDone = trackingSteps.Count > 0 && trackingSteps.All(s => s.IsDone);

        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            trackingMessage = "This order has been cancelled";
        }
        else if (allStepsDone)
        {
            trackingMessage = packageName switch
            {
                PackageNames.DoorToDoor => "Your bags have been delivered successfully to your address. Thank you for using Travora! 🎉",
                PackageNames.CarServiceToAirport => "Your bags have been loaded on the aircraft. Have a safe flight! ✈️",
                PackageNames.CarServiceFromAirport => "Your bags have been delivered successfully to your address. Thank you for using Travora! 🎉",
                PackageNames.TrackingBaggage => "Your bags are ready for pickup at the baggage belt. Thank you for using Travora! 🧳",
                _ => null
            };
        }

        return new OrderDetailsResponse
        {
            OrderId = order.OrderId,
            PackageName = packageName,
            Status = statusLabel,
            From = from,
            To = to,
            NumberOfBags = order.TotalBaggageCount,
            TotalWeight = totalWeight,
            NumberOfPassengers = order.OrderCompanions.Count + 1,
            CanCancel = canCancel,
            HasBoardingPass = hasBoardingPass,
            Appointment = appointment,
            TrackingStatus = trackingSteps,
            TrackingMessage = trackingMessage
        };
    }

    // ===================================================================
    // ENDPOINT 2 — Cancel Order
    // ===================================================================
    public async Task<CancelOrderResponse> CancelOrderAsync(int customerId, int orderId, string reason, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.OrderServices)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null)
            return new CancelOrderResponse { Success = false, Message = "Order not found" };

        if (order.CustomerId != customerId)
            return new CancelOrderResponse { Success = false, Message = "You do not have permission to cancel this order" };

        if (order.OrderStatus == OrderStatus.Completed)
            return new CancelOrderResponse { Success = false, Message = "A completed order cannot be cancelled" };

        if (order.OrderStatus == OrderStatus.Cancelled)
            return new CancelOrderResponse { Success = false, Message = "Order is already cancelled" };

        var hasActiveService = order.OrderServices
            .Any(os => os.ServiceStatus == ServiceStatus.InProgress);
        if (hasActiveService)
            return new CancelOrderResponse
            {
                Success = false,
                Message = "Order cannot be cancelled while the service is in progress"
            };

        bool shouldRefund = order.OrderStatus is OrderStatus.Confirmed or OrderStatus.InProgress or OrderStatus.rescheduled;
        bool refundSuccess = false;

        if (shouldRefund)
        {
            try
            {
                // Call RefundService FIRST while the order is still in Confirmed/rescheduled/InProgress state
                var refundRequest = new Travora.Application.DTOs.Refunds.RefundRequest { Reason = reason };
                var refundResult = await _refundService.RequestRefundAsync(customerId, orderId, refundRequest);
                
                if (refundResult.Success)
                {
                    refundSuccess = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto refund failed for order {OrderId} during cancellation", orderId);
            }
        }

        // Cancel the main order and services in our system
        // Note: If refundSuccess is true, the refund service already updated order.OrderStatus to Cancelled 
        // in the DB, but updating it here on the tracked entity is safe and ensures consistency before saving.
        order.OrderStatus = OrderStatus.Cancelled;
        order.CancellationReason = reason;
        order.UpdatedAt = DateTime.UtcNow;

        foreach (var svc in order.OrderServices)
        {
            if (svc.ServiceStatus is ServiceStatus.Pending or ServiceStatus.Assigned)
            {
                svc.ServiceStatus = ServiceStatus.Cancelled;
                svc.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Notification to customer (DB + real-time)
        // If refundSuccess is true, the refund service has already sent the "RefundApproved" push/notification, 
        // so we don't send a duplicate general cancel notification to the customer.
        if (!refundSuccess)
        {
            _context.Notifications.Add(new Domain.Entities.Notification
            {
                UserId = customerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.OrderUpdated,
                Title = "Your order has been cancelled",
                Message = "Your order has been cancelled successfully",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = orderId
            });

            await _notificationPusher.PushToCustomerAsync(customerId, "Your order has been cancelled", "Your order has been cancelled successfully", "OrderCancelled", orderId);
        }

        // Notification to assigned employee/driver (if any) - ALWAYS send this to free the drivers!
        var assignedServices = order.OrderServices.Where(os => os.AssignedEmployeeId.HasValue).ToList();
        foreach (var svc in assignedServices)
        {
            var empId = svc.AssignedEmployeeId!.Value;
            _context.Notifications.Add(new Domain.Entities.Notification
            {
                UserId = empId,
                UserType = UserType.Employee,
                NotificationType = NotificationType.OrderUpdated,
                Title = "Order Cancelled",
                Message = "The order assigned to you has been cancelled",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = orderId
            });
            await _notificationPusher.PushToEmployeeAsync(empId, "Order Cancelled", "The order assigned to you has been cancelled", "OrderCancelled", orderId);
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (refundSuccess)
        {
            return new CancelOrderResponse { Success = true, Message = "Your order has been cancelled and refunded successfully" };
        }

        return new CancelOrderResponse { Success = true, Message = "Your order has been cancelled successfully" };
    }

    // ===================================================================
    // ENDPOINT 3 — Available Dates for Reschedule
    // ===================================================================
    public async Task<AvailableDatesResponse> GetAvailableDatesForRescheduleAsync(
        int customerId, int orderId, RescheduleType type, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null)
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Order not found" };

        if (order.CustomerId != customerId)
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "You do not have permission" };

        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.rescheduled))
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "The date for this order cannot be changed" };

        var packageName = order.Package?.PackageName ?? string.Empty;

        if (type == RescheduleType.Pickup)
        {
            if (packageName != PackageNames.DoorToDoor && packageName != PackageNames.CarServiceToAirport)
                return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Rescheduling pickup is only available for Door to Door or Car Service to Airport services" };

            var departureTime = order.Flight.ScheduledDepartureTime;
            var today = DateTime.UtcNow.Date;
            var flightDate = departureTime.Date;

            if (flightDate < today)
                return new AvailableDatesResponse { IsValid = false, ErrorMessage = "The flight date has already passed" };

            var availableDates = new List<DateTime>();
            for (var d = today; d <= flightDate; d = d.AddDays(1))
            {
                // Must be at least 12 hours before departure
                if ((departureTime - d.Date).TotalHours >= 12)
                {
                    availableDates.Add(d);
                }
            }

            return new AvailableDatesResponse
            {
                IsValid = true,
                AvailableDates = availableDates
            };
        }
        else if (type == RescheduleType.Delivery)
        {
            if (packageName != PackageNames.DoorToDoor && packageName != PackageNames.CarServiceFromAirport)
                return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Rescheduling delivery is only available for Door to Door or Car Service from Airport services" };

            var arrivalTime = order.Flight.ScheduledArrivalTime;
            var executionStart = arrivalTime.AddHours(4);
            var executionEnd = executionStart.AddDays(4);
            var today = DateTime.UtcNow.Date;

            var startDate = executionStart.Date < today ? today : executionStart.Date;
            var availableDates = new List<DateTime>();

            for (var d = startDate; d <= executionEnd.Date; d = d.AddDays(1))
            {
                availableDates.Add(d);
            }

            return new AvailableDatesResponse
            {
                IsValid = true,
                AvailableDates = availableDates
            };
        }

        return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Invalid reschedule type" };
    }

    // ===================================================================
    // ENDPOINT 3.5 — Available Slots for Reschedule
    // ===================================================================
    public async Task<AvailableSlotsResponse> GetAvailableSlotsForRescheduleAsync(
        int customerId, int orderId, RescheduleType type, DateTime date, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Order not found" };

        if (order.CustomerId != customerId)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "You do not have permission" };

        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.rescheduled))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "The date for this order cannot be changed" };

        var packageName = order.Package?.PackageName ?? string.Empty;

        if (type == RescheduleType.Pickup)
        {
            if (packageName != PackageNames.DoorToDoor && packageName != PackageNames.CarServiceToAirport)
                return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Rescheduling pickup is only available for Door to Door or Car Service to Airport services" };
        }
        else if (type == RescheduleType.Delivery)
        {
            if (packageName != PackageNames.DoorToDoor && packageName != PackageNames.CarServiceFromAirport)
                return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Rescheduling delivery is only available for Door to Door or Car Service from Airport services" };
        }
        else
        {
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Invalid reschedule type" };
        }

        var today = DateTime.UtcNow.Date;
        if (date.Date < today)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Cannot select a date in the past" };

        var response = new AvailableSlotsResponse { IsValid = true };
        TimeSpan? cutoffTimeSpan = null;
        TimeSpan? startAfterTimeSpan = null;

        if (type == RescheduleType.Pickup)
        {
            var departureTime = order.Flight.ScheduledDepartureTime;
            if ((departureTime - date.Date).TotalHours < 12)
                return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Booking must be made at least 12 hours before departure" };

            var flightDate = departureTime.Date;
            if (date.Date > flightDate)
                return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Booking cannot be made after the flight date" };

            if (date.Date == flightDate)
            {
                var cutoffUtc = departureTime.AddHours(-12);
                cutoffTimeSpan = cutoffUtc.TimeOfDay;
                response.CutoffTime = cutoffTimeSpan.Value.ToString(@"hh\:mm");
                response.Note = $"The last available slot must end before {response.CutoffTime}";
            }
        }
        else // Delivery
        {
            var arrivalTime = order.Flight.ScheduledArrivalTime;
            var executionStart = arrivalTime.AddHours(4);
            var executionEnd = executionStart.AddDays(4);

            if (date.Date < executionStart.Date)
                return new AvailableSlotsResponse { IsValid = false, ErrorMessage = $"Cannot select a date before the earliest arrival-based window ({executionStart:yyyy-MM-dd})" };

            if (date.Date > executionEnd.Date)
                return new AvailableSlotsResponse { IsValid = false, ErrorMessage = $"Cannot book more than 4 days after delivery start window ({executionEnd:yyyy-MM-dd})" };

            if (date.Date == executionStart.Date)
            {
                startAfterTimeSpan = executionStart.TimeOfDay;
                response.Note = $"Nearest available delivery after {startAfterTimeSpan.Value:hh\\:mm}";
            }
        }

        var allDrivers = await _context.Employees
            .Include(e => e.Vehicle)
            .Where(e => e.JobRole == JobRole.Driver 
                     && e.IsActive 
                     && !e.IsDeleted
                     && e.VehicleId != null
                     && e.Vehicle!.IsActive
                     && !e.Vehicle.IsDeleted)
            .Include(e => e.AssignedOrderServices)
            .ToListAsync(cancellationToken);

        var slots = new List<string>
        {
            "00:00-02:00", "02:00-04:00", "04:00-06:00", "06:00-08:00",
            "08:00-10:00", "10:00-12:00", "12:00-14:00", "14:00-16:00",
            "16:00-18:00", "18:00-20:00", "20:00-22:00", "22:00-24:00"
        };

        foreach (var slot in slots)
        {
            var parts = slot.Split('-');
            var start = TimeSpan.Parse(parts[0]);
            var end = parts[1] == "24:00" ? TimeSpan.FromHours(24) : TimeSpan.Parse(parts[1]);

            bool isAvailable = true;

            if (type == RescheduleType.Pickup)
            {
                if (cutoffTimeSpan.HasValue && end > cutoffTimeSpan.Value)
                {
                    isAvailable = false;
                }
            }
            else // Delivery
            {
                if (startAfterTimeSpan.HasValue && start < startAfterTimeSpan.Value)
                {
                    isAvailable = false;
                }
            }

            if (isAvailable)
            {
                var availableDrivers = allDrivers.Where(d =>
                    IsShiftCovering(d.ShiftType, start, end) &&
                    !HasConflict(d, date.Date, start, end)
                ).ToList();

                if (!availableDrivers.Any())
                    isAvailable = false;
            }

            response.AvailableSlots.Add(new SlotItem { Slot = slot, Available = isAvailable });
        }

        response.AvailableSlots = response.AvailableSlots.Where(s => s.Available).ToList();
        return response;
    }

    // ===================================================================
    // ENDPOINT 4 — Reschedule Order
    // ===================================================================
    public async Task<RescheduleResponse> RescheduleOrderAsync(int customerId, int orderId, RescheduleRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight)
            .Include(o => o.OrderServices).ThenInclude(os => os.PackageService)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null)
            return new RescheduleResponse { Success = false, Message = "Order not found" };

        if (order.CustomerId != customerId)
            return new RescheduleResponse { Success = false, Message = "You do not have permission" };

        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.rescheduled))
            return new RescheduleResponse { Success = false, Message = "The date for this order cannot be changed" };

        var packageName = order.Package?.PackageName ?? string.Empty;

        if (request.Type == RescheduleType.Pickup)
        {
            if (packageName != PackageNames.DoorToDoor && packageName != PackageNames.CarServiceToAirport)
                return new RescheduleResponse { Success = false, Message = "Rescheduling pickup is only available for Door to Door or Car Service to Airport services" };
        }
        else if (request.Type == RescheduleType.Delivery)
        {
            if (packageName != PackageNames.DoorToDoor && packageName != PackageNames.CarServiceFromAirport)
                return new RescheduleResponse { Success = false, Message = "Rescheduling delivery is only available for Door to Door or Car Service from Airport services" };
        }
        else
        {
            return new RescheduleResponse { Success = false, Message = "Invalid reschedule type" };
        }

        // Validate slot is available
        var slotsResponse = await GetAvailableSlotsForRescheduleAsync(customerId, orderId, request.Type, request.NewDate, cancellationToken);
        var chosenSlot = slotsResponse.AvailableSlots.FirstOrDefault(s => s.Slot == request.NewTimeSlot);
        if (chosenSlot == null || !chosenSlot.Available)
            return new RescheduleResponse { Success = false, Message = "This time slot is not available" };

        // Parse slot times
        var slotParts = request.NewTimeSlot.Split('-');
        var slotStart = TimeSpan.Parse(slotParts[0]);
        var slotEnd = slotParts[1] == "24:00" ? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)) : TimeSpan.Parse(slotParts[1]);

        if (request.Type == RescheduleType.Delivery)
        {
            order.DeliveryDate = request.NewDate;
            order.DeliveryTimeSlot = request.NewTimeSlot;

            var deliveryService = order.OrderServices
                .FirstOrDefault(os => os.PackageService?.ExecutionPhase == ExecutionPhase.Delivery);
            if (deliveryService != null)
            {
                deliveryService.ScheduledStartTime = request.NewDate.Date + slotStart;
                deliveryService.ScheduledEndTime = request.NewDate.Date + slotEnd;
            }
        }
        else
        {
            order.PickupDate = request.NewDate;
            order.PickupTimeSlot = request.NewTimeSlot;

            var pickupService = order.OrderServices
                .FirstOrDefault(os => os.PackageService?.ExecutionPhase == ExecutionPhase.Pickup);
            if (pickupService != null)
            {
                pickupService.ScheduledStartTime = request.NewDate.Date + slotStart;
                pickupService.ScheduledEndTime = request.NewDate.Date + slotEnd;
            }
        }

        order.OrderStatus = OrderStatus.rescheduled;
        order.UpdatedAt = DateTime.UtcNow;

        // Notification — customer
        _context.Notifications.Add(new Domain.Entities.Notification
        {
            UserId = customerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = "Appointment rescheduled successfully",
            Message = "Appointment rescheduled successfully",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = orderId
        });
        await _notificationPusher.PushToCustomerAsync(customerId, "Appointment rescheduled successfully", "Appointment rescheduled successfully", "OrderRescheduled", orderId);

        // Notification — assigned driver (if any)
        var assignedServices = order.OrderServices
            .Where(os => os.AssignedEmployeeId.HasValue)
            .ToList();
        foreach (var svc in assignedServices)
        {
            var empId = svc.AssignedEmployeeId!.Value;
            _context.Notifications.Add(new Domain.Entities.Notification
            {
                UserId = empId,
                UserType = UserType.Employee,
                NotificationType = NotificationType.OrderUpdated,
                Title = "Order Appointment Changed",
                Message = "The appointment for the order assigned to you has been changed",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = orderId
            });
            await _notificationPusher.PushToEmployeeAsync(empId, "Order Appointment Changed", "The appointment for the order assigned to you has been changed", "OrderRescheduled", orderId);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new RescheduleResponse
        {
            Success = true,
            NewDate = request.NewDate.ToString("yyyy-MM-dd"),
            NewTimeSlot = request.NewTimeSlot,
            Message = "Appointment rescheduled successfully"
        };
    }

    // ===================================================================
    // ENDPOINT 5 — Boarding Pass
    // ===================================================================
    public async Task<BoardingPassResponse> GetBoardingPassAsync(int customerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight)
            .Include(o => o.Customer)
            .Include(o => o.OrderCompanions).ThenInclude(oc => oc.Companion)
            .Include(o => o.BoardingPasses)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null)
            throw new KeyNotFoundException("Order not found");

        if (order.CustomerId != customerId)
            throw new UnauthorizedAccessException("You do not have permission");

        var packageName = order.Package?.PackageName ?? string.Empty;
        if (packageName is not (PackageNames.DoorToDoor or PackageNames.CarServiceToAirport))
            throw new InvalidOperationException("Boarding pass is not available for this type of order");

        if (order.OrderStatus != OrderStatus.Confirmed && order.OrderStatus != OrderStatus.rescheduled && order.OrderStatus != OrderStatus.InProgress && order.OrderStatus != OrderStatus.Completed)
            throw new InvalidOperationException("Payment must be completed first");

        // If already generated, return from DB
        if (order.BoardingPasses.Any())
            return MapBoardingPasses(order);

        // Generate from airline API
        await GenerateAndSaveBoardingPassesAsync(order, cancellationToken);

        // Reload
        await _context.Entry(order).Collection(o => o.BoardingPasses).LoadAsync(cancellationToken);
        return MapBoardingPasses(order);
    }

    // ===================================================================
    // ENDPOINT 6 — Download Boarding Pass PDF
    // ===================================================================
    public async Task<(byte[] PdfBytes, string FileName)> DownloadBoardingPassAsync(int customerId, int orderId, CancellationToken cancellationToken = default)
    {
        // Ensure boarding passes exist
        var boardingPassResponse = await GetBoardingPassAsync(customerId, orderId, cancellationToken);

        var document = Document.Create(container =>
        {
            foreach (var pass in boardingPassResponse.BoardingPasses)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A5.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Content().Column(col =>
                    {
                        // Header
                        col.Item().Background(Colors.Blue.Darken3).Padding(10).Row(row =>
                        {
                            row.RelativeItem().Text(pass.AirlineName).Bold().FontSize(14).FontColor(Colors.White);
                            row.RelativeItem().AlignRight().Text("BOARDING PASS").Bold().FontSize(14).FontColor(Colors.White);
                        });

                        col.Item().PaddingVertical(5);

                        // Flight info
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"FROM: {pass.From} ({pass.FromCity})").Bold();
                                c.Item().Text($"TO: {pass.To} ({pass.ToCity})").Bold();
                                c.Item().Text($"FLIGHT: {pass.FlightNumber}");
                                c.Item().Text($"DATE: {pass.FlightDate}");
                                c.Item().Text($"DURATION: {pass.Duration}");
                            });
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"DEPARTURE: {pass.DepartureTime}");
                                c.Item().Text($"ARRIVAL: {pass.ArrivalTime}");
                                c.Item().Text($"TERMINAL: {pass.Terminal}");
                                c.Item().Text($"GATE: {pass.Gate}");
                            });
                        });

                        col.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);

                        // Passenger info
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"PASSENGER: {pass.PassengerName}").Bold().FontSize(12);
                                c.Item().Text($"SEAT: {pass.SeatNumber}").Bold().FontSize(12);
                                c.Item().Text($"CLASS: {pass.Class}");
                                c.Item().Text($"BOARDING TIME: {pass.BoardingTime}");
                            });
                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text("BARCODE").FontSize(8).FontColor(Colors.Grey.Darken1);
                                c.Item().Text(pass.BarcodeData).FontSize(7);
                            });
                        });
                    });
                });
            }
        });

        var pdfBytes = document.GeneratePdf();
        var fileName = $"BoardingPass_Order_{orderId}.pdf";

        return (pdfBytes, fileName);
    }

    // ===================================================================
    // Background — Generate Boarding Passes (for webhook)
    // ===================================================================
    public async Task GenerateBoardingPassesAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Flight)
            .Include(o => o.OrderCompanions).ThenInclude(oc => oc.Companion)
            .Include(o => o.BoardingPasses)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null || order.BoardingPasses.Any())
            return;

        await GenerateAndSaveBoardingPassesAsync(order, cancellationToken);
    }

    // ===================================================================
    // HELPERS
    // ===================================================================
    private async Task GenerateAndSaveBoardingPassesAsync(Domain.Entities.Order order, CancellationToken cancellationToken)
    {
        var flight = order.Flight;
        var tickets = new List<(string TicketNumber, string PassengerName, int? CustomerId, int? CompanionId)>();

        // Primary customer
        var customerName = $"{order.Customer.Firstname} {order.Customer.Lastname}";
        tickets.Add((order.TicketNumber ?? string.Empty, customerName, order.CustomerId, null));

        // Companions
        foreach (var oc in order.OrderCompanions)
        {
            var companion = oc.Companion;
            if (companion != null)
            {
                var compName = $"{companion.Firstname} {companion.Lastname}";
                tickets.Add((oc.TicketNumber ?? string.Empty, compName, null, companion.CompanionId));
            }
        }

        // Call airline API in parallel
        var tasks = tickets.Select(t => new
        {
            t.TicketNumber,
            t.PassengerName,
            t.CustomerId,
            t.CompanionId,
            Task = _airlineService.IssueBoardingPassAsync(t.TicketNumber, cancellationToken)
        }).ToList();

        await Task.WhenAll(tasks.Select(t => t.Task));

        foreach (var t in tasks)
        {
            var result = t.Task.Result;
            var boardingPass = new Domain.Entities.BoardingPass
            {
                OrderId = order.OrderId,
                FlightId = flight.FlightId,
                CustomerId = t.CustomerId,
                CompanionId = t.CompanionId,
                TicketNumber = t.TicketNumber,
                PassengerName = !string.IsNullOrEmpty(result.PassengerName) ? result.PassengerName : t.PassengerName,
                SeatNumber = result.SeatNumber ?? string.Empty,
                Gate = !string.IsNullOrEmpty(result.Gate) ? result.Gate : flight.DepartureGate ?? string.Empty,
                Terminal = !string.IsNullOrEmpty(result.Terminal) ? result.Terminal : flight.DepartureTerminal ?? string.Empty,
                Class = result.Class ?? string.Empty,
                BoardingTime = TimeSpan.TryParse(result.BoardingTime, out var bt) ? bt : flight.ScheduledDepartureTime.AddMinutes(-30).TimeOfDay,
                FlightDate = DateTime.TryParse(result.FlightDate, out var fd) ? fd : flight.ScheduledDepartureTime.Date,
                BarcodeData = result.BarcodeData ?? string.Empty,
                BoardingStatus = BoardingStatus.NotBoarded,
                IssuedAt = DateTime.UtcNow
            };

            _context.BoardingPasses.Add(boardingPass);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static BoardingPassResponse MapBoardingPasses(Domain.Entities.Order order)
    {
        var flight = order.Flight;
        var duration = flight.ScheduledArrivalTime - flight.ScheduledDepartureTime;
        var durationStr = $"{(int)duration.TotalHours}h {duration.Minutes}m";

        return new BoardingPassResponse
        {
            BoardingPasses = order.BoardingPasses.Select(bp => new BoardingPassItem
            {
                AirlineName = flight.AirlineName,
                FlightNumber = flight.FlightNumber,
                From = flight.DepartureIataCode,
                FromCity = flight.DepartureAirport?.NameAirport ?? flight.DepartureIataCode,
                To = flight.ArrivalIataCode,
                ToCity = flight.ArrivalAirport?.NameAirport ?? flight.ArrivalIataCode,
                Duration = durationStr,
                DepartureTime = flight.ScheduledDepartureTime.ToString("hh:mm tt"),
                ArrivalTime = flight.ScheduledArrivalTime.ToString("hh:mm tt"),
                PassengerName = bp.PassengerName,
                SeatNumber = bp.SeatNumber,
                Terminal = bp.Terminal,
                Gate = bp.Gate,
                Class = bp.Class,
                BoardingTime = new DateTime(bp.FlightDate.Year, bp.FlightDate.Month, bp.FlightDate.Day)
                    .Add(bp.BoardingTime).ToString("hh:mm tt"),
                FlightDate = bp.FlightDate.ToString("dd MMM yyyy").ToUpper(),
                BarcodeData = bp.BarcodeData
            }).ToList()
        };
    }

    private static string FormatSlotTime(string timeSlot)
    {
        if (string.IsNullOrEmpty(timeSlot)) return string.Empty;
        var parts = timeSlot.Split('-');
        if (parts.Length < 1) return timeSlot;
        if (TimeSpan.TryParse(parts[0], out var ts))
            return new DateTime(2000, 1, 1).Add(ts).ToString("hh:mm tt");
        return timeSlot;
    }

    // --- Shared slot helpers (same logic as DoorToDoorOrderService) ---
    private static bool IsShiftCovering(ShiftType shift, TimeSpan slotStart, TimeSpan slotEnd)
    {
        return shift switch
        {
            ShiftType.Morning => slotStart >= TimeSpan.FromHours(8) && slotEnd <= TimeSpan.FromHours(16),
            ShiftType.Evening => slotStart >= TimeSpan.FromHours(16) && slotEnd <= TimeSpan.FromHours(24),
            ShiftType.Night => slotStart >= TimeSpan.Zero && slotEnd <= TimeSpan.FromHours(8),
            ShiftType.rotating => true,
            _ => false
        };
    }

    private static bool HasConflict(Domain.Entities.Employee driver, DateTime date, TimeSpan slotStart, TimeSpan slotEnd)
    {
        return driver.AssignedOrderServices.Any(os =>
            os.ScheduledStartTime.Date == date &&
            os.ScheduledStartTime.TimeOfDay < slotEnd &&
            os.ScheduledEndTime.TimeOfDay > slotStart
        );
    }
}
