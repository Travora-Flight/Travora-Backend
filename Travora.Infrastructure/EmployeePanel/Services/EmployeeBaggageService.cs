using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Travora.Shared.Settings;
using Travora.Application.DTOs.Employee.Baggage;
using Travora.Application.Interfaces.External.FileStorage;
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
    private readonly IConnectionMultiplexer _redis;
    private readonly AirlineApiSettings _airlineSettings;

    public EmployeeBaggageService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        ICloudinaryService cloudinary,
        IConnectionMultiplexer redis,
        IOptions<AirlineApiSettings> airlineSettings)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _cloudinary = cloudinary;
        _redis = redis;
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
        baggage.UpdatedAt = DateTime.UtcNow;

        // 4) Get GPS from Redis
        decimal? gpsLat = null, gpsLng = null;
        var redisDb = _redis.GetDatabase();
        var locationJson = await redisDb.StringGetAsync($"employee:{employeeId}:last_location");
        if (locationJson.HasValue)
        {
            var loc = JsonSerializer.Deserialize<RedisLocationData>(locationJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
        _db.Notifications.Add(new Notification
        {
            UserId = order.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.BaggagePickedUp,
            Title = "تم استلام شنطتك",
            Message = "السواق استلم شنطتك وفي الطريق للمطار",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = order.OrderId,
            BaggageId = baggage.BaggageId
        });

        await _db.SaveChangesAsync();

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

        // Validate photo count
        var minPhotos = employee.JobRole == JobRole.Driver ? 3 : 3;
        if (photos.Count < minPhotos)
            throw new InvalidOperationException($"يجب رفع {minPhotos} صور على الأقل");

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
        var redisDb = _redis.GetDatabase();
        var locationJson = await redisDb.StringGetAsync($"employee:{employeeId}:last_location");
        if (locationJson.HasValue)
        {
            var loc = JsonSerializer.Deserialize<RedisLocationData>(locationJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
