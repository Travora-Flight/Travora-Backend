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
            throw new UnauthorizedAccessException("مش مسموح");

        if (orderService.ServiceStatus != ServiceStatus.InProgress)
            throw new InvalidOperationException("ابدأ الطلب الأول");

        var baggage = await _db.Baggages.FindAsync(request.BaggageId)
            ?? throw new KeyNotFoundException("Baggage not found");

        var alreadyScanned = await _db.QrScans
            .AnyAsync(q => q.BaggageId == baggage.BaggageId);
        if (alreadyScanned)
            throw new InvalidOperationException("هذه الشنطة تم مسحها مسبقاً");

        if (baggage.OrderId != orderService.OrderId)
            throw new InvalidOperationException("الشنطة مش في الأوردر ده");

        // 1) Call Airline API
        var client = _httpClientFactory.CreateClient("AirlineApi");
        var apiResponse = await client.GetAsync($"/api/airline/verify-baggage/{request.QrData}");

        if (!apiResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("رقم الشنطة غير موجود في نظام الطيران");

        var content = await apiResponse.Content.ReadAsStringAsync();
        var airlineResult = JsonSerializer.Deserialize<AirlineVerifyResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (airlineResult == null || !airlineResult.Valid)
            throw new InvalidOperationException("رقم الشنطة غير موجود في نظام الطيران");

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
                throw new UnauthorizedAccessException("صاحب الشنطة مش مسجل في الأوردر");
            }
        }

        // 3) Check tag uniqueness
        var tag = airlineResult.Tag!;
        if (!string.IsNullOrEmpty(tag.TagNumber))
        {
            var existingBag = await _db.Baggages
                .FirstOrDefaultAsync(b => b.BaggageNumber == tag.TagNumber && b.BaggageId != baggage.BaggageId);
            if (existingBag != null)
                throw new InvalidOperationException($"Tag number {tag.TagNumber} is already assigned to another baggage");
        }

        // 4) Update baggage in DB
        baggage.BaggageNumber = tag.TagNumber;
        baggage.TotalWeight = tag.WeightKg;
        baggage.Destination = tag.Destination;
        baggage.UpdatedAt = DateTime.UtcNow;

        // 4) Get GPS from Redis
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

        // 5) Record QR Scan
        var scan = new QrScan
        {
            BaggageId = baggage.BaggageId,
            ScannedByEmployeeId = employeeId,
            CheckpointId = null, // Driver has no checkpoint
            ScanTimestamp = DateTime.UtcNow,
            GpsLatitude = gpsLat ?? 0,
            GpsLongitude = gpsLng ?? 0
        };
        _db.QrScans.Add(scan);
        await _db.SaveChangesAsync();

        // 6) Record Baggage Tracking
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

        // 7) Notification
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
                var title = "تم استلام جميع شنطك";
                var message = $"السواق استلم {totalBags} شنطة وفي الطريق للمطار";

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
            .Include(b => b.BaggagePhotos)
            .FirstOrDefaultAsync(b =>
                b.BaggageId == baggageId &&
                b.Order.OrderServices.Any(os => os.AssignedEmployeeId == employeeId))
            ?? throw new UnauthorizedAccessException("هذه الشنطة مش في طلب مرتبط بيك");

        if (baggage.BaggageNumber == null)
            throw new InvalidOperationException("يجب سكان الشنطة الأول");

        // Requires active lock before uploading photos
        var hasActiveLock = await _db.SecurityLocks
            .AnyAsync(l => l.BaggageId == baggageId && l.IsActive && !l.IsDeleted);
        
        if (!hasActiveLock)
            throw new InvalidOperationException("يجب تسجيل كود القفل الأول");

        // Validate photo count (max 6 per baggage) directly against the DB
        var existingCount = await _db.BaggagePhotos.CountAsync(p => p.BaggageId == baggageId);
        if (existingCount >= 6)
            throw new InvalidOperationException("وصلت للحد الأقصى 6 صور لهذه الشنطة");

        var allowedToAdd = 6 - existingCount;
        if (photos.Count > allowedToAdd)
            throw new InvalidOperationException($"يمكن إضافة {allowedToAdd} صور فقط، الشنطة عندها {existingCount} صور");

        if (photos.Count < 3 && existingCount == 0)
            throw new InvalidOperationException("يجب رفع 3 صور على الأقل للبدء");

        // Validate file types
        var allowedTypes = new[] { "image/jpg", "image/jpeg", "image/png" };
        if (photos.Any(p => !allowedTypes.Contains(p.ContentType.ToLower())))
            throw new InvalidOperationException("يجب رفع صور فقط (jpg/jpeg/png)");

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
                CheckpointId = employee.CheckpointId, // null for Driver
                CaptureTimestamp = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        // Customer Notification — Photos uploaded
        var photoOrder = baggage.Order;
        _db.Notifications.Add(new Notification
        {
            UserId = photoOrder.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.BaggageUpdated,
            Title = "Photos uploaded for your baggage",
            Message = $"The employee uploaded {photos.Count} photos for bag {baggage.BaggageNumber}",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = photoOrder.OrderId,
            BaggageId = baggageId
        });
        await _db.SaveChangesAsync();

        await _pusher.PushToCustomerAsync(
            photoOrder.CustomerId,
            "Photos uploaded for your baggage",
            $"The employee uploaded {photos.Count} photos for bag {baggage.BaggageNumber}",
            "BaggageUpdated",
            photoOrder.OrderId);

        var totalPhotos = baggage.BaggagePhotos.Count + photos.Count;

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
            throw new UnauthorizedAccessException("هذه الإجراء غير متاح لك حالياً. يجب أن تكون مخصصاً للطلب وأن يكون قيد التنفيذ.");

        if (baggage.BaggageNumber == null)
            throw new InvalidOperationException("يجب مسح الشنطة الأول (Scan) قبل تعيين القفل.");

        if (baggage.SecurityLocks.Any(l => l.IsActive))
            throw new InvalidOperationException("هذه الشنطة مربوطة بقفل نشط بالفعل.");

        if (string.IsNullOrWhiteSpace(request.LockCode) || request.LockCode.Length != 9 || !request.LockCode.StartsWith("112371"))
            throw new InvalidOperationException("كود القفل غير صحيح، يجب أن يتكون من 9 أرقام ويبدأ بـ 112371.");

        if (!request.LockCode.All(char.IsDigit))
            throw new InvalidOperationException("كود القفل يجب أن يحتوي على أرقام فقط.");

        // Check if lock code is already active on another bag
        var lockExists = await _db.SecurityLocks
            .AnyAsync(l => l.LockCode == request.LockCode && l.IsActive && !l.IsDeleted);

        if (lockExists)
            throw new InvalidOperationException("هذا القفل مستخدم حالياً مع شنطة أخرى.");

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

        // Customer Notification — Lock registered
        var lockOrder = baggage.Order;
        _db.Notifications.Add(new Notification
        {
            UserId = lockOrder.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.BaggageUpdated,
            Title = "Lock registered on your baggage",
            Message = $"A security lock has been applied to bag {baggage.BaggageNumber}",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = lockOrder.OrderId,
            BaggageId = baggageId
        });
        await _db.SaveChangesAsync();

        await _pusher.PushToCustomerAsync(
            lockOrder.CustomerId,
            "Lock registered on your baggage",
            $"A security lock has been applied to bag {baggage.BaggageNumber}",
            "BaggageUpdated",
            lockOrder.OrderId);

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
            throw new UnauthorizedAccessException("هذا الإجراء للـ Baggage Handler فقط");

        if (employee.CheckpointId == null || employee.Checkpoint == null)
            throw new InvalidOperationException("مفيش checkpoint معين للموظف");

        var baggage = await _db.Baggages
            .Include(b => b.Order)
            .FirstOrDefaultAsync(b => b.BaggageNumber == request.BaggageTagNumber)
            ?? throw new KeyNotFoundException("رقم الشنطة غير موجود");

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

        // Notification
        _db.Notifications.Add(new Notification
        {
            UserId = baggage.Order.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.BaggageUpdated,
            Title = "تحديث شنطتك",
            Message = $"شنطتك الآن في {employee.Checkpoint.CheckpointName}",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = baggage.OrderId,
            BaggageId = baggage.BaggageId
        });

        await _db.SaveChangesAsync();

        // SignalR Real-time
        await _pusher.PushToCustomerAsync(
            baggage.Order.CustomerId,
            "تحديث شنطتك",
            $"شنطتك الآن في {employee.Checkpoint.CheckpointName}",
            "BaggageUpdated",
            baggage.OrderId);

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
