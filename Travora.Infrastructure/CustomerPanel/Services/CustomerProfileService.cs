using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using Travora.Application.DTOs.Customer.Profile;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Application.Interfaces.Services.Customer;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.CustomerPanel.Services;

public class CustomerProfileService : ICustomerProfileService
{
    private readonly ApplicationDbContext _db;
    private readonly IUpstashRedisService _redis;
    private readonly ICloudinaryService _cloudinary;

    public CustomerProfileService(
        ApplicationDbContext db,
        IUpstashRedisService redis,
        ICloudinaryService cloudinary)
    {
        _db = db;
        _redis = redis;
        _cloudinary = cloudinary;
    }

    // ── GET Profile ──────────────────────────────────────────────────────────
    public async Task<CustomerProfileResponse> GetProfileAsync(int customerId)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);

        if (customer == null) throw new Exception("Customer not found.");

        return new CustomerProfileResponse
        {
            CustomerId      = customer.CustomerId,
            FirstName       = customer.Firstname,
            LastName        = customer.Lastname,
            ProfileImageUrl = customer.ProfileImagePath,
            Email           = customer.Email,
            AccountStatus   = customer.AccountStatus.ToString()
        };
    }

    // ── GET Account ──────────────────────────────────────────────────────────
    public async Task<CustomerAccountResponse> GetAccountInfoAsync(int customerId)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);

        if (customer == null) throw new Exception("Customer not found.");

        string maskedPassport = customer.PassportNumber;
        if (!string.IsNullOrEmpty(maskedPassport))
        {
            maskedPassport = maskedPassport.Length < 4
                ? new string('*', maskedPassport.Length)
                : maskedPassport[..4] + "****";
        }

        return new CustomerAccountResponse
        {
            FirstName      = customer.Firstname,
            LastName       = customer.Lastname,
            MobileNumber   = customer.PhoneNumber,
            Gender         = customer.Gender,
            DateOfBirth    = customer.DateOfBirth.ToString("dd/MM/yyyy"),
            PassportNumber = maskedPassport
        };
    }

    // ── PUT Account (Partial Update — text fields only) ──────────────────────
    public async Task<(bool Success, string Message)> UpdateAccountAsync(
        int customerId, UpdateAccountRequest request, IFormFile? profileImage)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId);
        if (customer == null)
            return (false, "Customer not found");

        // Validate MobileNumber — required when provided
        if (!string.IsNullOrEmpty(request.MobileNumber))
        {
            var cleaned = request.MobileNumber.Trim().Replace(" ", "").Replace("-", "");
            var isValid =
                Regex.IsMatch(cleaned, @"^01[0-9]{9}$")     ||
                Regex.IsMatch(cleaned, @"^\+2001[0-9]{9}$") ||
                Regex.IsMatch(cleaned, @"^002001[0-9]{9}$");

            if (!isValid)
                return (false, "Invalid phone number");

            customer.PhoneNumber = cleaned;
        }

        if (request.FirstName != null) customer.Firstname = request.FirstName;
        if (request.LastName  != null) customer.Lastname  = request.LastName;

        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (true, "Account data updated successfully");
    }

    // ── POST Upload Photo ────────────────────────────────────────────────────
    public async Task<UploadPhotoResponse> UploadPhotoAsync(int customerId, IFormFile photo)
    {
        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png" };
        if (!allowedTypes.Contains(photo.ContentType.ToLower()))
            throw new InvalidOperationException("Only JPG or PNG images are allowed");

        const long maxSize = 5L * 1024 * 1024; // 5 MB
        if (photo.Length > maxSize)
            throw new InvalidOperationException("Image size must not exceed 5MB");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId)
            ?? throw new KeyNotFoundException("Customer not found");

        // Delete old photo from Cloudinary if exists
        if (!string.IsNullOrEmpty(customer.ProfileImagePath))
        {
            var oldPublicId = _cloudinary.ExtractPublicId(customer.ProfileImagePath);
            await _cloudinary.DeleteFileAsync(oldPublicId);
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var publicId  = $"customer_{customerId}_{timestamp}";

        string newUrl;
        using (var stream = photo.OpenReadStream())
            newUrl = await _cloudinary.UploadFileAsync(stream, publicId, "travora/customers/profiles");

        customer.ProfileImagePath = newUrl;
        customer.UpdatedAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new UploadPhotoResponse { Success = true, ProfileImageUrl = newUrl };
    }

    // ── DELETE Photo ─────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> DeletePhotoAsync(int customerId)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId)
            ?? throw new KeyNotFoundException("Customer not found");

        if (string.IsNullOrEmpty(customer.ProfileImagePath))
            return (false, "No photo to delete");

        var publicId = _cloudinary.ExtractPublicId(customer.ProfileImagePath);
        await _cloudinary.DeleteFileAsync(publicId);

        customer.ProfileImagePath = null;
        customer.UpdatedAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (true, "Photo deleted successfully");
    }

    // ── GET Settings (Redis) ─────────────────────────────────────────────────
    public async Task<CustomerSettingsResponse> GetSettingsAsync(int customerId)
    {
        var key   = $"customer:{customerId}:settings";
        var value = await _redis.GetAsync(key);

        if (!string.IsNullOrEmpty(value))
        {
            try
            {
                return JsonSerializer.Deserialize<CustomerSettingsResponse>(value)
                       ?? new CustomerSettingsResponse();
            }
            catch { /* fall through to defaults */ }
        }

        return new CustomerSettingsResponse();
    }

    // ── PUT Settings (Redis — persistent, no TTL) ────────────────────────────
    public async Task<bool> UpdateSettingsAsync(int customerId, CustomerSettingsRequest request)
    {
        var key   = $"customer:{customerId}:settings";
        var value = JsonSerializer.Serialize(request);

        await _redis.SetAsync(key, value); // No expiry = persistent
        return true;
    }

    // ── GET Orders ───────────────────────────────────────────────────────────
    public async Task<CustomerOrdersResponse> GetOrdersAsync(int customerId)
    {
        var orders = await _db.Orders
            .Include(o => o.Package)
            .Include(o => o.Customer)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        if (!orders.Any())
            return new CustomerOrdersResponse { Message = "No Orders Found" };

        var orderDtos = orders.Select(o =>
        {
            string name        = o.Package?.PackageName ?? "";
            string packageType = "unknown";

            if      (name.Contains("Door To Door",    StringComparison.OrdinalIgnoreCase)) packageType = "door_to_door";
            else if (name.Contains("To Airport",      StringComparison.OrdinalIgnoreCase)) packageType = "car_service";
            else if (name.Contains("From Airport",    StringComparison.OrdinalIgnoreCase)) packageType = "car_service";
            else if (name.Contains("Car Service",     StringComparison.OrdinalIgnoreCase)) packageType = "car_service";
            else if (name.Contains("Track My Bags",   StringComparison.OrdinalIgnoreCase)) packageType = "bag_tracking";

            return new CustomerOrderDto
            {
                OrderId     = o.OrderId,
                PackageName = name,
                PackageType = packageType,
                OrderStatus = o.OrderStatus.ToString().ToLower(),
                CreatedAt   = o.CreatedAt
            };
        }).ToList();

        return new CustomerOrdersResponse { Orders = orderDtos };
    }

    // ── GET Saved Flights ────────────────────────────────────────────────────
    public async Task<SavedFlightsResponse> GetSavedFlightsAsync(int customerId)
    {
        var flights = await _db.SavedFlights
            .Include(sf => sf.Flight)
            .Where(sf => sf.CustomerId == customerId && sf.IsActive)
            .ToListAsync();

        if (!flights.Any())
            return new SavedFlightsResponse { Message = "No Flights Found" };

        var dtos = flights.Select(sf => new SavedFlightDto
        {
            SavedFlightId       = sf.SavedFlightId,
            FlightNumber        = sf.Flight?.FlightNumber ?? "",
            From                = sf.Flight?.DepartureIataCode ?? "",
            To                  = sf.Flight?.ArrivalIataCode ?? "",
            FlightDate          = sf.Flight?.ScheduledDepartureTime.ToString("dd MMM yyyy") ?? "",
            DepartureTime       = sf.Flight?.ScheduledDepartureTime.ToString("hh:mm tt") ?? "",
            Status              = sf.Flight?.FlightStatus.ToString() ?? "scheduled",
            AirlineName         = sf.Flight?.AirlineName ?? "",
            NotificationEnabled = sf.NotificationEnabled
        }).ToList();

        return new SavedFlightsResponse { SavedFlights = dtos };
    }

    // ── POST Save Flight (reactivate if soft-deleted) ────────────────────────
    public async Task<(bool Success, string Message, int? SavedFlightId)> SaveFlightAsync(int customerId, int flightId)
    {
        var flightExists = await _db.Flights.AnyAsync(f => f.FlightId == flightId);
        if (!flightExists)
            return (false, "Flight not found", null);

        var existing = await _db.SavedFlights
            .FirstOrDefaultAsync(sf => sf.CustomerId == customerId && sf.FlightId == flightId);

        if (existing != null)
        {
            if (existing.IsActive)
                return (false, "Flight already saved", null);

            existing.IsActive             = true;
            existing.NotificationEnabled  = true;
            existing.SavedAt              = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (true, "Saved", existing.SavedFlightId);
        }

        var newSavedFlight = new SavedFlight
        {
            CustomerId          = customerId,
            FlightId            = flightId,
            NotificationEnabled = true,
            IsActive            = true,
            SavedAt             = DateTime.UtcNow
        };

        _db.SavedFlights.Add(newSavedFlight);
        await _db.SaveChangesAsync();

        return (true, "Saved", newSavedFlight.SavedFlightId);
    }

    // ── DELETE Saved Flight (soft delete) ────────────────────────────────────
    public async Task<(bool Success, string Message)> RemoveSavedFlightAsync(int customerId, int savedFlightId)
    {
        var savedFlight = await _db.SavedFlights
            .FirstOrDefaultAsync(sf => sf.SavedFlightId == savedFlightId && sf.CustomerId == customerId);

        if (savedFlight == null)
            return (false, "Flight not found or unauthorized");

        savedFlight.IsActive = false;
        await _db.SaveChangesAsync();

        return (true, "Flight removed");
    }

    // ── PATCH Toggle Notification ─────────────────────────────────────────────
    public async Task<(bool Success, string Message, bool? NotificationEnabled)> ToggleFlightNotificationAsync(int customerId, int savedFlightId)
    {
        var savedFlight = await _db.SavedFlights
            .FirstOrDefaultAsync(sf => sf.SavedFlightId == savedFlightId && sf.CustomerId == customerId && sf.IsActive);

        if (savedFlight == null)
            return (false, "Flight not found", null);

        savedFlight.NotificationEnabled = !savedFlight.NotificationEnabled;
        await _db.SaveChangesAsync();

        return (true, "Updated successfully", savedFlight.NotificationEnabled);
    }

    // ── POST Add Payment Method ───────────────────────────────────────────────
    public async Task<(bool Success, string Message, object? Data)> AddPaymentMethodAsync(int customerId, AddPaymentMethodRequest request)
    {
        var currentYear  = DateTime.UtcNow.Year % 100;
        var currentMonth = DateTime.UtcNow.Month;

        if (request.ExpiryMonth < 1 || request.ExpiryMonth > 12)
            return (false, "Invalid expiry month", null);

        if (request.ExpiryYear < currentYear || (request.ExpiryYear == currentYear && request.ExpiryMonth < currentMonth))
            return (false, "The card is expired", null);

        string lastFour = request.CardNumber.Length >= 4
            ? request.CardNumber[^4..]
            : request.CardNumber;

        string brand = request.CardNumber.FirstOrDefault() switch
        {
            '4' => "Visa",
            '5' => "Mastercard",
            '3' => "Amex",
            _   => "Unknown"
        };

        var isDuplicate = await _db.PaymentMethods.AnyAsync(pm =>
            pm.CustomerId      == customerId          &&
            pm.CardLastFour    == lastFour            &&
            pm.CardExpiryMonth == request.ExpiryMonth &&
            pm.CardExpiryYear  == request.ExpiryYear  &&
            pm.CardBrand       == brand               &&
            pm.IsActive        &&
            !pm.IsDeleted);

        if (isDuplicate)
            return (false, "This card is already added", null);

        bool isFirstCard = !await _db.PaymentMethods.AnyAsync(pm =>
            pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted);

        PaymentFunding fundingEnum = PaymentFunding.Credit;
        Enum.TryParse<PaymentFunding>(request.PaymentFunding, true, out fundingEnum);

        var newMethod = new PaymentMethod
        {
            CustomerId      = customerId,
            CardHolderName  = request.CardHolderName,
            CardLastFour    = lastFour,
            CardBrand       = brand,
            CardExpiryMonth = request.ExpiryMonth,
            CardExpiryYear  = request.ExpiryYear,
            PaymentFunding  = fundingEnum,
            IsDefault       = isFirstCard,
            IsActive        = true,
            IsDeleted       = false,
            AddedAt         = DateTime.UtcNow,
            CreatedAt       = DateTime.UtcNow
        };

        _db.PaymentMethods.Add(newMethod);
        await _db.SaveChangesAsync();

        var resultData = new
        {
            paymentMethodId = newMethod.PaymentMethodId,
            cardBrand       = brand,
            cardLastFour    = lastFour,
            isDefault       = isFirstCard
        };

        return (true, "Card added successfully", resultData);
    }

    // ── POST Change Password ────────────────────────────────────────────────
    public async Task ChangePasswordAsync(int customerId, string currentPassword, string newPassword, string confirmPassword)
    {
        var customer = await _db.Customers.FindAsync(customerId)
            ?? throw new KeyNotFoundException("Customer not found");

        if (string.IsNullOrEmpty(customer.PasswordHash) || !BCrypt.Net.BCrypt.Verify(currentPassword, customer.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect");

        if (newPassword != confirmPassword)
            throw new ArgumentException("Passwords do not match");

        if (currentPassword == newPassword)
            throw new ArgumentException("New password cannot be the same as the current password");

        // Password strength validation
        if (newPassword.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters");
        if (!newPassword.Any(char.IsUpper))
            throw new ArgumentException("Password must contain an uppercase letter");
        if (!newPassword.Any(char.IsLower))
            throw new ArgumentException("Password must contain a lowercase letter");
        if (!newPassword.Any(char.IsDigit))
            throw new ArgumentException("Password must contain a number");
        if (!newPassword.Any(c => !char.IsLetterOrDigit(c)))
            throw new ArgumentException("Password must contain a symbol (!@#$%^&*)");

        customer.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
