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
        [PackageNames.DoorToDoor] = new()
        {
            ("Order Confirmed",    BaggageTrackingStatus.Registered,       "db",      "تم تأكيد الطلب وجدولة الخدمة"),
            ("Picked Up",          BaggageTrackingStatus.PickedUp,         "db",      "تم استلام الشنط من عنوانك"),
            ("Check-In",           BaggageTrackingStatus.PickedUp,         "airline", "تم تسجيل الشنط في المطار"),
            ("Security Check",     BaggageTrackingStatus.AtSecurity,       "airline", "الشنط في الفحص الأمني"),
            ("At Customs",         BaggageTrackingStatus.AtCustoms,        "airline", "الشنط في الجمارك"),
            ("At Gate",            BaggageTrackingStatus.AtGate,           "airline", "الشنط عند البوابة"),
            ("Loaded on Aircraft", BaggageTrackingStatus.LoadedOnAircraft, "airline", "تم تحميل الشنط على الطائرة"),
            ("Arrived",            BaggageTrackingStatus.Arrived,          "airline", "وصلت الطائرة للوجهة"),
            ("Out for Delivery",   BaggageTrackingStatus.OutForDelivery,   "db",      "الشنط في الطريق إليك"),
            ("Delivered",          BaggageTrackingStatus.Delivered,        "db",      "تم التسليم بنجاح")
        },
        [PackageNames.CarServiceToAirport] = new()
        {
            ("Order Confirmed",    BaggageTrackingStatus.Registered,       "db",      "تم تأكيد الطلب"),
            ("Picked Up",          BaggageTrackingStatus.PickedUp,         "db",      "تم استلام الشنط من عنوانك"),
            ("Check-In",           BaggageTrackingStatus.PickedUp,         "airline", "تم تسجيل الشنط في المطار"),
            ("Security Check",     BaggageTrackingStatus.AtSecurity,       "airline", "الشنط في الفحص الأمني"),
            ("At Gate",            BaggageTrackingStatus.AtGate,           "airline", "الشنط عند البوابة"),
            ("Loaded on Aircraft", BaggageTrackingStatus.LoadedOnAircraft, "airline", "تم تحميل الشنط على الطائرة"),
            ("Arrived",            BaggageTrackingStatus.Arrived,          "airline", "وصلت الشنط للوجهة")
        },
        [PackageNames.CarServiceFromAirport] = new()
        {
            ("Order Confirmed",    BaggageTrackingStatus.Registered,      "db",      "تم تأكيد الطلب"),
            ("Arrived",            BaggageTrackingStatus.Arrived,         "airline", "وصلت الطائرة"),
            ("Baggage Belt",       BaggageTrackingStatus.OnBelt,          "airline", "الشنط على سير الأمتعة"),
            ("Out for Delivery",   BaggageTrackingStatus.OutForDelivery,  "db",      "الشنط في الطريق إليك"),
            ("Delivered",          BaggageTrackingStatus.Delivered,       "db",      "تم التسليم بنجاح")
        },
        [PackageNames.TrackingBaggage] = new()
        {
            ("Bags Registered",    BaggageTrackingStatus.Registered,       "db",      "placeholder"),
            ("Check-In",           BaggageTrackingStatus.PickedUp,         "airline", "تم تسجيل الشنط في المطار"),
            ("Security Check",     BaggageTrackingStatus.AtSecurity,       "airline", "الشنط في الفحص الأمني"),
            ("At Customs",         BaggageTrackingStatus.AtCustoms,        "airline", "الشنط في الجمارك"),
            ("At Terminal",        BaggageTrackingStatus.AtTerminal,       "airline", "الشنط في الصالة"),
            ("At Gate",            BaggageTrackingStatus.AtGate,           "airline", "الشنط عند البوابة"),
            ("Loaded on Aircraft", BaggageTrackingStatus.LoadedOnAircraft, "airline", "تم تحميل الشنط على الطائرة"),
            ("Arrived",            BaggageTrackingStatus.Arrived,          "airline", "وصلت الطائرة للوجهة"),
            ("Ready for Pickup",   BaggageTrackingStatus.OnBelt,           "airline", "الشنط جاهزة للاستلام من سير الأمتعة")
        }
    };

    private static readonly Dictionary<string, BaggageTrackingStatus> LocationToStatusMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Check-In"]           = BaggageTrackingStatus.PickedUp,
        ["Security Check"]     = BaggageTrackingStatus.AtSecurity,
        ["Customs"]            = BaggageTrackingStatus.AtCustoms,
        ["Terminal"]           = BaggageTrackingStatus.AtTerminal,
        ["Gate"]               = BaggageTrackingStatus.AtGate,
        ["Loaded on Aircraft"] = BaggageTrackingStatus.LoadedOnAircraft,
        ["Arrived"]            = BaggageTrackingStatus.Arrived,
        ["Baggage Belt"]       = BaggageTrackingStatus.OnBelt
    };

    // ===================================================================
    // ENDPOINT 0 — List Orders
    // ===================================================================
    public async Task<IEnumerable<OrderListDto>> GetCustomerOrdersAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var orders = await _context.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight)
            .Include(o => o.PickupLocation)
            .Include(o => o.DeliveryLocation)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var result = new List<OrderListDto>();
        foreach (var order in orders)
        {
            var packageName = order.Package?.PackageName ?? string.Empty;
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
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null)
            throw new KeyNotFoundException("الطلب غير موجود");

        if (order.CustomerId != customerId)
            throw new UnauthorizedAccessException("ليس لديك صلاحية لعرض هذا الطلب");

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
        }

        if (order.OrderStatus == OrderStatus.Pending)
        {
            var pendingSteps = TrackingStepsByPackage.TryGetValue(packageName, out var ps)
                ? ps.Select((s, index) => new TrackingStepDto
                  {
                      Step = s.Step,
                      Description = index == 0 ? $"{order.TotalBaggageCount} bags registered in system" : null,
                      IsDone = false,
                      Timestamp = null
                  }).ToList()
                : new List<TrackingStepDto>();

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
                TrackingStatus = pendingSteps
            };
        }

        // --- Tracking ---
        // 1) DB tracking — last status per bag
        var dbTrackingMap = new Dictionary<int, (BaggageTrackingStatus Status, DateTime ArrivalTime)>();
        foreach (var bag in order.Baggages)
        {
            var lastTracking = bag.BaggageTrackings
                .OrderByDescending(bt => bt.ArrivalTime)
                .FirstOrDefault();
            if (lastTracking != null)
                dbTrackingMap[bag.BaggageId] = (lastTracking.Status, lastTracking.ArrivalTime);
        }

        // 2) Airline tracking — call by-ticket for all tickets in parallel
        var airlineBagMap = new Dictionary<string, (BaggageTrackingStatus Status, DateTime? UpdatedAt)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var ticketNumbers = new List<string>();
            if (!string.IsNullOrEmpty(order.TicketNumber))
                ticketNumbers.Add(order.TicketNumber);
            foreach (var oc in order.OrderCompanions)
                if (!string.IsNullOrEmpty(oc.TicketNumber))
                    ticketNumbers.Add(oc.TicketNumber);

            var airlineTasks = ticketNumbers
                .Select(tn => _airlineService.GetBaggageByTicketAsync(tn, cancellationToken))
                .ToList();

            var airlineResults = await Task.WhenAll(airlineTasks);

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

        // 3) Merge — MAX per bag, then MAX overall
        var overallStatus = BaggageTrackingStatus.Registered;
        // Map for airline timestamps keyed by status
        var airlineTimestamps = new Dictionary<BaggageTrackingStatus, DateTime?>();
        var dbTimestamps = new Dictionary<BaggageTrackingStatus, DateTime>();

        foreach (var bag in order.Baggages)
        {
            var dbStatus = dbTrackingMap.ContainsKey(bag.BaggageId) ? dbTrackingMap[bag.BaggageId].Status : BaggageTrackingStatus.Registered;
            var airlineStatus = BaggageTrackingStatus.Registered;
            DateTime? airlineTimestamp = null;

            if (!string.IsNullOrEmpty(bag.BaggageNumber) && airlineBagMap.TryGetValue(bag.BaggageNumber, out var airlineData))
            {
                airlineStatus = airlineData.Status;
                airlineTimestamp = airlineData.UpdatedAt;
            }

            var baggageStatus = (BaggageTrackingStatus)Math.Max((int)dbStatus, (int)airlineStatus);
            if ((int)baggageStatus > (int)overallStatus)
                overallStatus = baggageStatus;

            // Collect timestamps
            if (dbTrackingMap.ContainsKey(bag.BaggageId))
            {
                if (!dbTimestamps.ContainsKey(dbStatus) || dbTrackingMap[bag.BaggageId].ArrivalTime > dbTimestamps[dbStatus])
                    dbTimestamps[dbStatus] = dbTrackingMap[bag.BaggageId].ArrivalTime;
            }
            if (airlineTimestamp.HasValue && airlineStatus != BaggageTrackingStatus.Registered)
            {
                foreach (var statusValue in Enum.GetValues<BaggageTrackingStatus>())
                {
                    if ((int)statusValue <= (int)airlineStatus)
                    {
                        if (!airlineTimestamps.ContainsKey(statusValue) || airlineTimestamp > airlineTimestamps[statusValue])
                            airlineTimestamps[statusValue] = airlineTimestamp;
                    }
                }
            }
        }

        // Also collect all DB tracking timestamps per status (for steps)
        foreach (var bag in order.Baggages)
        {
            foreach (var bt in bag.BaggageTrackings)
            {
                if (!dbTimestamps.ContainsKey(bt.Status) || bt.ArrivalTime > dbTimestamps[bt.Status])
                    dbTimestamps[bt.Status] = bt.ArrivalTime;
            }
        }

        // Auto-Complete logic for Bag Tracking
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
                    Title = "شنطتك وصلت",
                    Message = "شنطتك جاهزة للاستلام من سير الأمتعة",
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = orderId
                });

                await _context.SaveChangesAsync(cancellationToken);

                await _notificationPusher.PushToCustomerAsync(
                    order.CustomerId,
                    "شنطتك وصلت",
                    "شنطتك جاهزة للاستلام من سير الأمتعة",
                    "OrderCompleted",
                    orderId);
                
                // Update local order variable for accurate response
                order.OrderStatus = OrderStatus.Completed;
            }
        }

        // Build tracking steps
        var trackingSteps = new List<TrackingStepDto>();
        if (TrackingStepsByPackage.TryGetValue(packageName, out var steps))
        {
            foreach (var (stepName, stepStatus, source, description) in steps)
            {
                bool isDone = (int)overallStatus >= (int)stepStatus;
                DateTime? timestamp = null;

                if (isDone)
                {
                    if (source == "db" && dbTimestamps.TryGetValue(stepStatus, out var dbTs))
                        timestamp = dbTs;
                    else if (source == "airline" && airlineTimestamps.TryGetValue(stepStatus, out var airTs))
                        timestamp = airTs;
                }

                string? stepDescription;
                if (stepName == "Bags Registered" && packageName == PackageNames.TrackingBaggage)
                    stepDescription = $"تم تسجيل {order.TotalBaggageCount} شنط في النظام";
                else
                    stepDescription = description;

                trackingSteps.Add(new TrackingStepDto
                {
                    Step = stepName,
                    Timestamp = timestamp,
                    IsDone = isDone,
                    Description = stepDescription
                });
            }
        }

        if (packageName == PackageNames.TrackingBaggage)
        {
            var firstStep = trackingSteps.FirstOrDefault();
            if (firstStep != null && firstStep.IsDone && firstStep.Timestamp == null)
            {
                var orderService = await _context.OrderServices
                    .Where(os => os.OrderId == orderId)
                    .FirstOrDefaultAsync(cancellationToken);
                
                if (orderService?.ActualStartTime != null)
                    firstStep.Timestamp = orderService.ActualStartTime;
            }
        }

        // --- Status label ---
        string statusLabel = overallStatus switch
        {
            BaggageTrackingStatus.Registered => "Order Confirmed",
            BaggageTrackingStatus.PickedUp => "Picked Up",
            BaggageTrackingStatus.AtCustoms => "At Customs",
            BaggageTrackingStatus.AtSecurity => "Security Check",
            BaggageTrackingStatus.AtTerminal => "At Terminal",
            BaggageTrackingStatus.AtGate => "At Gate",
            BaggageTrackingStatus.LoadedOnAircraft => "Loaded on Aircraft",
            BaggageTrackingStatus.Arrived => "Arrived",
            BaggageTrackingStatus.OnBelt => "Baggage Belt",
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
            TrackingStatus = trackingSteps
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
            return new CancelOrderResponse { Success = false, Message = "الطلب غير موجود" };

        if (order.CustomerId != customerId)
            return new CancelOrderResponse { Success = false, Message = "ليس لديك صلاحية لإلغاء هذا الطلب" };

        if (order.OrderStatus == OrderStatus.Completed)
            return new CancelOrderResponse { Success = false, Message = "لا يمكن إلغاء طلب مكتمل" };

        if (order.OrderStatus == OrderStatus.Cancelled)
            return new CancelOrderResponse { Success = false, Message = "الطلب ملغي بالفعل" };

        var hasActiveService = order.OrderServices
            .Any(os => os.ServiceStatus == ServiceStatus.InProgress);
        if (hasActiveService)
            return new CancelOrderResponse
            {
                Success = false,
                Message = "لا يمكن إلغاء الطلب أثناء تنفيذ الخدمة، يرجى الانتظار حتى انتهاء الخدمة الجارية"
            };

        bool shouldRefund = order.OrderStatus is OrderStatus.Confirmed or OrderStatus.InProgress;

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
        _context.Notifications.Add(new Domain.Entities.Notification
        {
            UserId = customerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = "تم إلغاء طلبك",
            Message = "تم إلغاء طلبك بنجاح",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = orderId
        });

        await _notificationPusher.PushToCustomerAsync(customerId, "تم إلغاء طلبك", "تم إلغاء طلبك بنجاح", "OrderCancelled", orderId);

        // Notification to assigned employee (if any)
        var assignedServices = order.OrderServices.Where(os => os.AssignedEmployeeId.HasValue).ToList();
        foreach (var svc in assignedServices)
        {
            var empId = svc.AssignedEmployeeId!.Value;
            _context.Notifications.Add(new Domain.Entities.Notification
            {
                UserId = empId,
                UserType = UserType.Employee,
                NotificationType = NotificationType.OrderUpdated,
                Title = "تم إلغاء الطلب",
                Message = "تم إلغاء الطلب المعين لك",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = orderId
            });
            await _notificationPusher.PushToEmployeeAsync(empId, "تم إلغاء الطلب", "تم إلغاء الطلب المعين لك", "OrderCancelled", orderId);
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (shouldRefund)
        {
            try
            {
                var refundRequest = new Travora.Application.DTOs.Refunds.RefundRequest { Reason = reason };
                await _refundService.RequestRefundAsync(customerId, orderId, refundRequest);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto refund failed for order {OrderId}", orderId);
            }
        }

        return new CancelOrderResponse { Success = true, Message = "تم إلغاء الطلب بنجاح" };
    }

    // ===================================================================
    // ENDPOINT 3 — Available Slots for Reschedule
    // ===================================================================
    public async Task<AvailableSlotsResponse> GetAvailableSlotsForRescheduleAsync(
        int customerId, int orderId, string type, DateTime date, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "الطلب غير موجود" };

        if (order.CustomerId != customerId)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "ليس لديك صلاحية" };

        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.rescheduled))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "لا يمكن تغيير موعد هذا الطلب" };

        var packageName = order.Package?.PackageName ?? string.Empty;

        if (string.Equals(type, "delivery", StringComparison.OrdinalIgnoreCase) && packageName != PackageNames.DoorToDoor)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "تغيير موعد التوصيل متاح فقط لخدمة Door To Door" };

        // 12-hour rule
        var departureTime = order.Flight.ScheduledDepartureTime;
        if ((departureTime - date.Date).TotalHours < 12)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "لا يمكن الحجز قبل أقل من 12 ساعة من الإقلاع" };

        var today = DateTime.UtcNow.Date;
        if (date.Date < today)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "لا يمكن اختيار يوم في الماضي" };

        var flightDate = departureTime.Date;
        if (date.Date > flightDate)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "لا يمكن الحجز بعد يوم الرحلة" };

        var response = new AvailableSlotsResponse();
        TimeSpan? cutoffTimeSpan = null;

        if (date.Date == flightDate)
        {
            var cutoffUtc = departureTime.AddHours(-12);
            cutoffTimeSpan = cutoffUtc.TimeOfDay;
            response.CutoffTime = cutoffTimeSpan.Value.ToString(@"hh\:mm");
            response.Note = $"آخر slot متاح يجب أن ينتهي قبل {response.CutoffTime}";
        }

        var allDrivers = await _context.Employees
            .Where(e => e.JobRole == JobRole.Driver && e.IsActive && !e.IsDeleted)
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

            if (cutoffTimeSpan.HasValue && end > cutoffTimeSpan.Value)
            {
                isAvailable = false;
            }
            else
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
            return new RescheduleResponse { Success = false, Message = "الطلب غير موجود" };

        if (order.CustomerId != customerId)
            return new RescheduleResponse { Success = false, Message = "ليس لديك صلاحية" };

        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.rescheduled))
            return new RescheduleResponse { Success = false, Message = "لا يمكن تغيير موعد هذا الطلب" };

        var packageName = order.Package?.PackageName ?? string.Empty;
        bool isDelivery = string.Equals(request.Type, "delivery", StringComparison.OrdinalIgnoreCase);

        if (isDelivery && packageName != PackageNames.DoorToDoor)
            return new RescheduleResponse { Success = false, Message = "تغيير موعد التوصيل متاح فقط لخدمة Door To Door" };

        // Validate slot is available
        var slotsResponse = await GetAvailableSlotsForRescheduleAsync(customerId, orderId, request.Type, request.NewDate, cancellationToken);
        var chosenSlot = slotsResponse.AvailableSlots.FirstOrDefault(s => s.Slot == request.NewTimeSlot);
        if (chosenSlot == null || !chosenSlot.Available)
            return new RescheduleResponse { Success = false, Message = "هذا الموعد غير متاح" };

        // Parse slot times
        var slotParts = request.NewTimeSlot.Split('-');
        var slotStart = TimeSpan.Parse(slotParts[0]);
        var slotEnd = slotParts[1] == "24:00" ? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)) : TimeSpan.Parse(slotParts[1]);

        if (isDelivery)
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
            Title = "تم تغيير الموعد بنجاح",
            Message = "تم تغيير الموعد بنجاح",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = orderId
        });
        await _notificationPusher.PushToCustomerAsync(customerId, "تم تغيير الموعد بنجاح", "تم تغيير الموعد بنجاح", "OrderRescheduled", orderId);

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
                Title = "تم تغيير موعد الطلب",
                Message = "تم تغيير موعد الطلب المعين لك",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = orderId
            });
            await _notificationPusher.PushToEmployeeAsync(empId, "تم تغيير موعد الطلب", "تم تغيير موعد الطلب المعين لك", "OrderRescheduled", orderId);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new RescheduleResponse
        {
            Success = true,
            NewDate = request.NewDate.ToString("yyyy-MM-dd"),
            NewTimeSlot = request.NewTimeSlot,
            Message = "تم تغيير الموعد بنجاح"
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
            throw new KeyNotFoundException("الطلب غير موجود");

        if (order.CustomerId != customerId)
            throw new UnauthorizedAccessException("ليس لديك صلاحية");

        var packageName = order.Package?.PackageName ?? string.Empty;
        if (packageName is not (PackageNames.DoorToDoor or PackageNames.CarServiceToAirport))
            throw new InvalidOperationException("بطاقة الصعود غير متاحة لهذا النوع من الطلبات");

        if (order.OrderStatus != OrderStatus.Confirmed && order.OrderStatus != OrderStatus.rescheduled && order.OrderStatus != OrderStatus.InProgress && order.OrderStatus != OrderStatus.Completed)
            throw new InvalidOperationException("يجب إتمام الدفع أولاً");

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
