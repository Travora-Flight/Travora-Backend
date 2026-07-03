using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travora.Application.Interfaces;
using Travora.Shared.Settings;
using Travora.Application.DTOs.Employee.Baggage;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Employee;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.EmployeePanel.Services;

public class EmployeeBaggageService : IEmployeeBaggageService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICloudinaryService _cloudinary;
    private readonly IUpstashRedisService _redis;
    private readonly INotificationPusher _pusher;
    private readonly AirlineApiSettings _airlineSettings;

    public EmployeeBaggageService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        ICloudinaryService cloudinary,
        IUpstashRedisService redis,
        INotificationPusher pusher,
        IOptions<AirlineApiSettings> airlineSettings)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _cloudinary = cloudinary;
        _redis = redis;
        _pusher = pusher;
        _airlineSettings = airlineSettings.Value;
    }

    public async Task<BaggageScanResponse> ScanBaggageAsync(int employeeId, BaggageScanRequest request)
    {
        // Verify employee is assigned to this order service
        var orderService = await _db.OrderServices
            .Include(os => os.Order).ThenInclude(o => o.Customer)
            .Include(os => os.Order).ThenInclude(o => o.OrderCompanions).ThenInclude(oc => oc.Companion)
            .FirstOrDefaultAsync(os => os.OrderServiceId == request.OrderServiceId)
            ?? throw new KeyNotFoundException("Task not found");

        if (orderService.AssignedEmployeeId != employeeId)
            throw new UnauthorizedAccessException("Unauthorized");

        if (orderService.ServiceStatus != ServiceStatus.InProgress)
            throw new InvalidOperationException("Please start the order first");

        var baggage = await _db.Baggages.FindAsync(request.BaggageId)
            ?? throw new KeyNotFoundException("Baggage not found");

        var alreadyScannedInPhase = await _db.QrScans
            .AnyAsync(q => q.BaggageId == baggage.BaggageId
                        && q.OrderServiceId == request.OrderServiceId);
        if (alreadyScannedInPhase)
            throw new InvalidOperationException("This bag has already been scanned in this phase");

        if (baggage.OrderId != orderService.OrderId)
            throw new InvalidOperationException("This bag is not in this order");

        // 1) Call Airline API
        var client = _httpClientFactory.CreateClient("AirlineApi");
        var apiResponse = await client.GetAsync($"/api/airline/verify-baggage/{request.QrData}");

        if (!apiResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("Bag number not found in airline system");

        var content = await apiResponse.Content.ReadAsStringAsync();
        var airlineResult = JsonSerializer.Deserialize<AirlineVerifyResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (airlineResult == null || !airlineResult.Valid)
            throw new InvalidOperationException("Bag number not found in airline system");

        // 2) Determine bag owner from passport
        var passportNumber = airlineResult.Passport;
        var order = orderService.Order;
        BaggageOwnerDto? owner = null;

        var customer = order.Customer;
        if (customer.PassportNumber == passportNumber)
        {
            owner = new BaggageOwnerDto { OwnerType = "customer", OwnerName = $"{customer.Firstname} {customer.Lastname}" };
            baggage.CustomerId = customer.CustomerId;
        }
        else
        {
            var companion = order.OrderCompanions
                .Select(oc => oc.Companion)
                .FirstOrDefault(c => c.PassportNumber == passportNumber);

            if (companion != null)
            {
                owner = new BaggageOwnerDto { OwnerType = "companion", OwnerName = $"{companion.Firstname} {companion.Lastname}" };
                baggage.CompanionId = companion.CompanionId;
            }
            else
            {
                throw new UnauthorizedAccessException("Bag owner is not registered in the order");
            }
        }

        // 3) Check tag uniqueness
        var tag = airlineResult.Tag!;
        if (!string.IsNullOrEmpty(tag.TagNumber))
        {
            var existingBag = await _db.Baggages
                .Include(b => b.Order)
                .FirstOrDefaultAsync(b => b.BaggageNumber == tag.TagNumber 
                                          && b.BaggageId != baggage.BaggageId
                                          && b.Order.OrderStatus != OrderStatus.Cancelled
                                          && b.Order.OrderStatus != OrderStatus.Completed
                                          && (b.Order.CustomerId != orderService.Order.CustomerId 
                                              || b.OrderId == baggage.OrderId));

            if (existingBag != null)
                throw new InvalidOperationException($"Tag number {tag.TagNumber} is already assigned to another baggage");
        }

        // 4) Verify tag matches if bag was already scanned in a previous phase
        if (!string.IsNullOrEmpty(baggage.BaggageNumber) && !string.IsNullOrEmpty(tag.TagNumber)
            && baggage.BaggageNumber != tag.TagNumber)
        {
            throw new InvalidOperationException(
                $"You must scan the same bag that was previously registered. Expected: {baggage.BaggageNumber}");
        }

        // 5) Update baggage in DB
        baggage.BaggageNumber = tag.TagNumber;
        baggage.TotalWeight = tag.WeightKg;
        baggage.Destination = tag.Destination;
        baggage.UpdatedAt = DateTime.UtcNow;

        // 6) Get GPS from Redis
        decimal? gpsLat = null, gpsLng = null;
        var locationJson = await _redis.GetAsync($"employee:{employeeId}:last_location");
        if (!string.IsNullOrEmpty(locationJson))
        {
            var loc = JsonSerializer.Deserialize<RedisLocationData>(locationJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (loc != null)
            {
                gpsLat = loc.Latitude;
                gpsLng = loc.Longitude;
            }
        }

        // 7) Record QR Scan
        var scan = new QrScan
        {
            BaggageId = baggage.BaggageId,
            ScannedByEmployeeId = employeeId,
            CheckpointId = null, // Driver has no checkpoint
            OrderServiceId = request.OrderServiceId,
            ScanTimestamp = DateTime.UtcNow,
            GpsLatitude = gpsLat ?? 0,
            GpsLongitude = gpsLng ?? 0
        };
        _db.QrScans.Add(scan);
        await _db.SaveChangesAsync();

        // 8) Record Baggage Tracking
        _db.BaggageTrackings.Add(new BaggageTracking
        {
            Status = BaggageTrackingStatus.PickedUp,
            HandledByEmployeeId = employeeId,
            BaggageId = baggage.BaggageId,
            CheckpointId = null,
            TriggeredByScanId = scan.ScanId,
            ArrivalTime = DateTime.UtcNow,
            GpsLatitude = gpsLat ?? 0,
            GpsLongitude = gpsLng ?? 0
        });

        // 9) Notification
        var orderWithBags = await _db.Orders
            .Include(o => o.Baggages)
                .ThenInclude(b => b.BaggageTrackings)
            .FirstOrDefaultAsync(o => o.OrderId == baggage.OrderId);

        if (orderWithBags != null)
        {
            var allBagsScanned = orderWithBags.Baggages.All(b =>
                b.BaggageTrackings.Any(bt =>
                    bt.Status == BaggageTrackingStatus.PickedUp));

            if (allBagsScanned)
            {
                var totalBags = orderWithBags.Baggages.Count;
                var title = "All your bags have been received";
                var message = $"The driver received {totalBags} bags and is on the way to the airport";

                // DB Notification
                _db.Notifications.Add(new Notification
                {
                    UserId = order.CustomerId,
                    UserType = UserType.Customer,
                    NotificationType = NotificationType.BaggagePickedUp,
                    Title = title,
                    Message = message,
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = order.OrderId,
                    BaggageId = null
                });

                await _db.SaveChangesAsync();

                // SignalR Real-time
                await _pusher.PushToCustomerAsync(
                    order.CustomerId,
                    title,
                    message,
                    "BaggagePickedUp",
                    order.OrderId);
            }
        }

        var now = DateTime.UtcNow;
        return new BaggageScanResponse
        {
            Success = true,
            Baggage = new ScannedBaggageDto
            {
                BaggageId = baggage.BaggageId,
                TagNumber = tag.TagNumber!,
                WeightKg = tag.WeightKg,
                Destination = tag.Destination,
                FlightNumber = tag.FlightNumber,
                Gate = tag.Gate,
                Terminal = tag.Terminal,
                PassengerName = tag.PassengerName,
                DepartureTime = tag.DepartureTime,
                BoardingTime = tag.BoardingTime,
                IsScanned = true,
                ScannedAt = now
            },
            Owner = owner
        };
    }

    public async Task<BaggagePhotoResponse> UploadBaggagePhotosAsync(int employeeId, int baggageId, List<IFormFile> photos)
    {
        var employee = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        // Verify baggage belongs to employee's order
        var baggage = await _db.Baggages
            .Include(b => b.Order).ThenInclude(o => o.OrderServices)
            .Include(b => b.Order).ThenInclude(o => o.Package)
            .Include(b => b.BaggagePhotos)
            .FirstOrDefaultAsync(b =>
                b.BaggageId == baggageId &&
                b.Order.OrderServices.Any(os => os.AssignedEmployeeId == employeeId))
            ?? throw new UnauthorizedAccessException("This bag is not in an order associated with you");

        if (baggage.BaggageNumber == null)
            throw new InvalidOperationException("Must scan the bag first");

        // Determine the active OrderService for this employee (needed for scoped photo limits)
        var activeOrderServiceId = baggage.Order.OrderServices
            .Where(os => os.AssignedEmployeeId == employeeId &&
                         os.ServiceStatus == ServiceStatus.InProgress)
            .Select(os => (int?)os.OrderServiceId)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No active task found for this employee");

        // Requires active lock before uploading photos (only for packages that have a Pickup phase)
        var requiresLock = baggage.Order.Package?.PackageCode == Travora.Domain.Constants.PackageCodes.DoorToDoor || 
                           baggage.Order.Package?.PackageCode == Travora.Domain.Constants.PackageCodes.CarServiceToAirport;

        if (requiresLock)
        {
            var hasActiveLock = await _db.SecurityLocks
                .AnyAsync(l => l.BaggageId == baggageId && l.IsActive && !l.IsDeleted);
            
            if (!hasActiveLock)
                throw new InvalidOperationException("Must register the lock code first");
        }

        // Validate photo count per THIS order service (each employee has their own limit of 6)
        var existingCount = await _db.BaggagePhotos
            .CountAsync(p => p.BaggageId == baggageId && p.OrderServiceId == activeOrderServiceId);
        if (existingCount >= 6)
            throw new InvalidOperationException("Reached the maximum limit of 6 photos for this bag in your task");

        var allowedToAdd = 6 - existingCount;
        if (photos.Count > allowedToAdd)
            throw new InvalidOperationException($"Only {allowedToAdd} photos can be added, you already have {existingCount} photos for this bag");

        if (photos.Count < 3 && existingCount == 0)
            throw new InvalidOperationException("At least 3 photos must be uploaded to start");

        // Validate file types
        var allowedTypes = new[] { "image/jpg", "image/jpeg", "image/png" };
        if (photos.Any(p => !allowedTypes.Contains(p.ContentType.ToLower())))
            throw new InvalidOperationException("Only images can be uploaded (jpg/jpeg/png)");


        var uploadedUrls = new List<string>();
        foreach (var photo in photos)
        {
            using var stream = photo.OpenReadStream();
            var url = await _cloudinary.UploadFileAsync(stream, photo.FileName, "travora/baggage");
            uploadedUrls.Add(url);

            _db.BaggagePhotos.Add(new BaggagePhoto
            {
                ImagePath = url,
                CapturedByEmployeeId = employeeId,
                BaggageId = baggageId,
                CheckpointId = employee.CheckpointId,
                OrderServiceId = activeOrderServiceId,
                CaptureTimestamp = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        // Customer Notification — Only when ALL bags in the order have photos for this phase
        var photoOrder = baggage.Order;
        var orderBaggageIds = await _db.Baggages
            .Where(b => b.OrderId == photoOrder.OrderId)
            .Select(b => b.BaggageId)
            .ToListAsync();

        var allBagsHavePhotos = true;
        foreach (var bid in orderBaggageIds)
        {
            var photoCount = await _db.BaggagePhotos
                .CountAsync(p => p.BaggageId == bid && p.OrderServiceId == activeOrderServiceId);
            if (photoCount < 3) { allBagsHavePhotos = false; break; }
        }

        if (allBagsHavePhotos)
        {
            var totalBags = orderBaggageIds.Count;
            var notifTitle = "Photos uploaded for all your bags";
            var notifMsg = $"The employee has uploaded photos for all {totalBags} bags in your order";

            _db.Notifications.Add(new Notification
            {
                UserId = photoOrder.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.BaggageUpdated,
                Title = notifTitle,
                Message = notifMsg,
                NotificationChannel = NotificationChannel.InApp,
                OrderId = photoOrder.OrderId
            });
            await _db.SaveChangesAsync();

            await _pusher.PushToCustomerAsync(
                photoOrder.CustomerId,
                notifTitle,
                notifMsg,
                "BaggageUpdated",
                photoOrder.OrderId);
        }

        var totalPhotos = existingCount + photos.Count;

        return new BaggagePhotoResponse
        {
            Success = true,
            BaggageId = baggageId,
            TagNumber = baggage.BaggageNumber,
            PhotosAdded = photos.Count,
            TotalPhotos = totalPhotos,
            Photos = uploadedUrls
        };
    }

    public async Task<LockBaggageResponse> AssignLockCodeAsync(int employeeId, int baggageId, LockBaggageRequest request)
    {
        var employee = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        var baggage = await _db.Baggages
            .Include(b => b.Order).ThenInclude(o => o.OrderServices)
            .Include(b => b.SecurityLocks)
            .FirstOrDefaultAsync(b => b.BaggageId == baggageId)
            ?? throw new KeyNotFoundException("Baggage not found");

        // Validations
        var hasAssignedService = baggage.Order.OrderServices
            .Any(os => os.AssignedEmployeeId == employeeId && os.ServiceStatus == ServiceStatus.InProgress);

        if (!hasAssignedService)
            throw new UnauthorizedAccessException("This action is not available to you currently. You must be assigned to the order and it must be in progress.");

        if (baggage.BaggageNumber == null)
            throw new InvalidOperationException("Must scan the bag first before setting the lock.");

        if (baggage.SecurityLocks.Any(l => l.IsActive))
            throw new InvalidOperationException("This bag is already linked to an active lock.");

        if (string.IsNullOrWhiteSpace(request.LockCode) || request.LockCode.Length != 7)
            throw new InvalidOperationException("Invalid lock code, it must consist of 7 digits.");

        if (!request.LockCode.All(char.IsDigit))
            throw new InvalidOperationException("Lock code must contain digits only.");

        // Check if lock code is already active on another bag
        var lockExists = await _db.SecurityLocks
            .AnyAsync(l => l.LockCode == request.LockCode && l.IsActive && !l.IsDeleted);

        if (lockExists)
            throw new InvalidOperationException("This lock is currently in use with another bag.");

        // Assigned new lock
        var newLock = new SecurityLock
        {
            LockCode = request.LockCode,
            AppliedAt = DateTime.UtcNow,
            IsActive = true,
            AppliedByEmployeeId = employeeId,
            BaggageId = baggageId
        };

        _db.SecurityLocks.Add(newLock);
        await _db.SaveChangesAsync();

        // Customer Notification — Only when ALL bags in the order have active locks
        var lockOrder = baggage.Order;
        var lockOrderBaggageIds = await _db.Baggages
            .Where(b => b.OrderId == lockOrder.OrderId)
            .Select(b => b.BaggageId)
            .ToListAsync();

        var allBagsLocked = true;
        foreach (var bid in lockOrderBaggageIds)
        {
            var hasLock = await _db.SecurityLocks
                .AnyAsync(l => l.BaggageId == bid && l.IsActive && !l.IsDeleted);
            if (!hasLock) { allBagsLocked = false; break; }
        }

        if (allBagsLocked)
        {
            var totalBags = lockOrderBaggageIds.Count;
            var notifTitle = "All your bags have been secured";
            var notifMsg = $"Security locks have been applied to all {totalBags} bags in your order";

            _db.Notifications.Add(new Notification
            {
                UserId = lockOrder.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.BaggageUpdated,
                Title = notifTitle,
                Message = notifMsg,
                NotificationChannel = NotificationChannel.InApp,
                OrderId = lockOrder.OrderId
            });
            await _db.SaveChangesAsync();

            await _pusher.PushToCustomerAsync(
                lockOrder.CustomerId,
                notifTitle,
                notifMsg,
                "BaggageUpdated",
                lockOrder.OrderId);
        }

        return new LockBaggageResponse
        {
            Success = true,
            BaggageId = baggage.BaggageId,
            LockCode = newLock.LockCode,
            Message = "Lock assigned successfully."
        };
    }

    public async Task<CheckpointUpdateResponse> UpdateCheckpointAsync(int employeeId, CheckpointUpdateRequest request)
    {
        var employee = await _db.Employees
            .Include(e => e.Checkpoint)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        if (employee.JobRole != JobRole.BaggageHandler)
            throw new UnauthorizedAccessException("This action is for the Baggage Handler only");

        if (employee.CheckpointId == null || employee.Checkpoint == null)
            throw new InvalidOperationException("No specific checkpoint for the employee");

        var baggage = await _db.Baggages
            .Include(b => b.Order)
            .FirstOrDefaultAsync(b => b.BaggageNumber == request.BaggageTagNumber)
            ?? throw new KeyNotFoundException("Bag tag number not found");

        // Determine status from checkpoint type
        var newStatus = employee.Checkpoint.CheckpointType switch
        {
            CheckpointType.PickupPoint => BaggageTrackingStatus.PickedUp,
            CheckpointType.Customs => BaggageTrackingStatus.AtCustoms,
            CheckpointType.SecurityCheck => BaggageTrackingStatus.AtSecurity,
            CheckpointType.AirportTerminal => BaggageTrackingStatus.AtTerminal,
            CheckpointType.AirportGate => BaggageTrackingStatus.AtGate,
            CheckpointType.AirportBaggageBelt => BaggageTrackingStatus.OnBelt,
            CheckpointType.DeliveryPoint => BaggageTrackingStatus.Delivered,
            CheckpointType.BaggageOffice => BaggageTrackingStatus.AtBaggageOffice,
            _ => BaggageTrackingStatus.AtTerminal
        };

        // Get GPS from Redis
        decimal? gpsLat = null, gpsLng = null;
        var locationJson = await _redis.GetAsync($"employee:{employeeId}:last_location");
        if (!string.IsNullOrEmpty(locationJson))
        {
            var loc = JsonSerializer.Deserialize<RedisLocationData>(locationJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (loc != null)
            {
                gpsLat = loc.Latitude;
                gpsLng = loc.Longitude;
            }
        }

        // Record QR Scan
        var scan = new QrScan
        {
            BaggageId = baggage.BaggageId,
            ScannedByEmployeeId = employeeId,
            CheckpointId = employee.CheckpointId,
            ScanTimestamp = DateTime.UtcNow,
            GpsLatitude = gpsLat ?? 0,
            GpsLongitude = gpsLng ?? 0
        };
        _db.QrScans.Add(scan);
        await _db.SaveChangesAsync();

        // Record Baggage Tracking
        var now = DateTime.UtcNow;
        _db.BaggageTrackings.Add(new BaggageTracking
        {
            Status = newStatus,
            HandledByEmployeeId = employeeId,
            BaggageId = baggage.BaggageId,
            CheckpointId = employee.CheckpointId,
            TriggeredByScanId = scan.ScanId,
            ArrivalTime = now,
            GpsLatitude = gpsLat ?? 0,
            GpsLongitude = gpsLng ?? 0
        });

        await _db.SaveChangesAsync();

        // Notification — BaggageOffice is always per-bag (urgent), otherwise per-order
        if (newStatus == BaggageTrackingStatus.AtBaggageOffice)
        {
            // Baggage Office is urgent — always notify immediately per bag
            var officeTitle = "Your bag is at the Baggage Office";
            var officeMsg = "Your bag has been moved to the lost baggage office. Please come with your passport to collect it.";

            _db.Notifications.Add(new Notification
            {
                UserId = baggage.Order.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.BaggageUpdated,
                Title = officeTitle,
                Message = officeMsg,
                NotificationChannel = NotificationChannel.InApp,
                OrderId = baggage.OrderId,
                BaggageId = baggage.BaggageId
            });
            await _db.SaveChangesAsync();

            await _pusher.PushToCustomerAsync(
                baggage.Order.CustomerId,
                officeTitle,
                officeMsg,
                "BaggageUpdated",
                baggage.OrderId);
        }
        else
        {
            // Normal checkpoint — notify only when ALL bags in the order have reached this checkpoint
            var cpOrderBaggageIds = await _db.Baggages
                .Where(b => b.OrderId == baggage.OrderId)
                .Select(b => b.BaggageId)
                .ToListAsync();

            var allBagsAtCheckpoint = true;
            foreach (var bid in cpOrderBaggageIds)
            {
                var hasStatus = await _db.BaggageTrackings
                    .AnyAsync(t => t.BaggageId == bid && t.Status == newStatus);
                if (!hasStatus) { allBagsAtCheckpoint = false; break; }
            }

            if (allBagsAtCheckpoint)
            {
                var totalBags = cpOrderBaggageIds.Count;
                var cpTitle = "Bag Update";
                var cpMsg = $"All {totalBags} bags are now at {employee.Checkpoint.CheckpointName}";

                _db.Notifications.Add(new Notification
                {
                    UserId = baggage.Order.CustomerId,
                    UserType = UserType.Customer,
                    NotificationType = NotificationType.BaggageUpdated,
                    Title = cpTitle,
                    Message = cpMsg,
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = baggage.OrderId
                });
                await _db.SaveChangesAsync();

                await _pusher.PushToCustomerAsync(
                    baggage.Order.CustomerId,
                    cpTitle,
                    cpMsg,
                    "BaggageUpdated",
                    baggage.OrderId);
            }
        }

        return new CheckpointUpdateResponse
        {
            Success = true,
            Baggage = new BaggageCheckpointInfoDto
            {
                TagNumber = baggage.BaggageNumber!,
                NewStatus = newStatus.ToString(),
                CheckpointName = employee.Checkpoint.CheckpointName,
                UpdatedAt = now,
                Notes = request.Notes
            }
        };
    }
}

// Internal DTOs for deserialization
internal class AirlineVerifyResponse
{
    public bool Valid { get; set; }
    public string? Passport { get; set; }
    public AirlineTagData? Tag { get; set; }
}

internal class AirlineTagData
{
    public string? TagNumber { get; set; }
    public decimal WeightKg { get; set; }
    public string? Destination { get; set; }
    public string? FlightNumber { get; set; }
    public string? Origin { get; set; }
    public string? Gate { get; set; }
    public string? Terminal { get; set; }
    public string? PassengerName { get; set; }
    public DateTime? DepartureTime { get; set; }
    public DateTime? BoardingTime { get; set; }
}

internal class RedisLocationData
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal? Speed { get; set; }
    public decimal? Heading { get; set; }
    public bool IsMoving { get; set; }
}
