using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.External.Airline;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.Interfaces.External;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Customer;
using Travora.Domain.Constants;
using Travora.Domain.Enums;
using Travora.Domain.Entities;
using Travora.Infrastructure.Data;
using System.Text.Json;
using Travora.Application.DTOs.Customer.Auth;
using Travora.Infrastructure.Helpers;

namespace Travora.Infrastructure.CustomerPanel.Services;

public class DoorToDoorOrderService : IDoorToDoorOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IAirlineService _airlineService;
    private readonly ICloudinaryService _cloudinaryService;
    private readonly IDraftOrderService _draftOrderService;
    private readonly IGeocodingService _geocodingService;
    private readonly INotificationPusher _pusher;
    private readonly IPassportOcrService _ocrService;
    private readonly IFlightPredictionService _predictionService;
 
    public DoorToDoorOrderService(
        ApplicationDbContext context,
        IAirlineService airlineService,
        ICloudinaryService cloudinaryService,
        IDraftOrderService draftOrderService,
        IGeocodingService geocodingService,
        INotificationPusher pusher,
        IPassportOcrService ocrService,
        IFlightPredictionService predictionService)
    {
        _context = context;
        _airlineService = airlineService;
        _cloudinaryService = cloudinaryService;
        _draftOrderService = draftOrderService;
        _geocodingService = geocodingService;
        _pusher = pusher;
        _ocrService = ocrService;
        _predictionService = predictionService;
    }

    public async Task<ValidateFlightResponse> ValidateFlightAsync(int customerId, ValidateFlightRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Get passport number for the current user
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
            
        if (customer == null)
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "Customer not found." };
            
        if (string.IsNullOrEmpty(customer.PassportNumber))
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "Customer passport number is missing. Please complete your profile." };
            
        if (customer.AccountStatus != Domain.Enums.CustomerAccountStatus.Verified)
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "Your account must be verified to use this service." };

        if (request.BaggageCount <= 0)
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "Please enter the number of bags." };

        // 2. Call the airline service
        var airlineReq = new AirlineValidateTicketRequest
        {
            PassportNumber = customer.PassportNumber,
            TicketNumber = request.TicketNumber,
            FlightNumber = request.FlightNumber,
            FlightDate = request.FlightDate
        };

        var airlineRes = await _airlineService.ValidateTicketAsync(airlineReq, cancellationToken);

        var flightData = airlineRes.Flight ?? airlineRes.Ticket?.Flight ?? airlineRes.FlightInfo;
        var passengerData = airlineRes.Passenger ?? airlineRes.Ticket?.Passenger ?? airlineRes.PassengerInfo;

        if (!airlineRes.IsValid || flightData == null || passengerData == null)
        {
            var errorMsg = airlineRes.Errors != null && airlineRes.Errors.Any()
                ? string.Join(", ", airlineRes.Errors)
                : "Invalid flight or ticket details from airline.";
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = errorMsg };
        }

        // Prevent booking Door To Door if ANY active order exists for this ticket (cross-service check)
        try
        {
            await ValidateTicketNotUsedAsync(request.TicketNumber, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = ex.Message };
        }

        flightData.Terminal = airlineRes.Terminal ?? airlineRes.Ticket?.Flight?.Terminal ?? flightData.Terminal;
        flightData.Gate = airlineRes.Gate ?? airlineRes.Ticket?.Flight?.Gate ?? flightData.Gate;
        flightData.FlightDate = airlineRes.FlightDate ?? flightData.FlightDate;
        flightData.FlightDuration = airlineRes.FlightDuration ?? flightData.FlightDuration;
        flightData.BoardingTimeUtc = airlineRes.BoardingTimeUtc ?? flightData.BoardingTimeUtc;

        passengerData.SeatNumber = airlineRes.Ticket?.SeatNumber ?? passengerData.SeatNumber;
        passengerData.TravelClass = airlineRes.Ticket?.TravelClass ?? passengerData.TravelClass;
        passengerData.BoardingStatus = airlineRes.Ticket?.BoardingStatus ?? passengerData.BoardingStatus;

        // 3. Validation rule: check if departure is at least 12 hours from now
        var departure = flightData.DepartureTimeUtc;
        var diff = departure - DateTime.UtcNow;
        if (diff.TotalHours < 12)
        {
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "Booking must be made at least 12 hours before departure" };
        }

        var bookingDeadlineUtc = departure.AddHours(-12);

        // 4. Update the draft order in Redis
        var draft = new DraftOrder
        {
            CustomerId = customerId.ToString(),
            TicketNumber = request.TicketNumber,
            FlightInfo = flightData,
            PassengerInfo = passengerData,
            BaggageCount = request.BaggageCount,
            BookingDeadlineUtc = bookingDeadlineUtc
        };

        await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new ValidateFlightResponse
        {
            IsValid = true,
            FlightInfo = flightData,
            PassengerInfo = passengerData,
            BaggageCount = request.BaggageCount,
            BookingDeadlineUtc = bookingDeadlineUtc
        };
    }

    public async Task<ValidateCompanionResponse> ValidateCompanionAsync(int customerId, ValidateCompanionRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Fetch draft order to get the primary flight number
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
        {
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "Order session expired or not found. Please restart the process." };
        }

        // 2. Validate passport image via OCR + Upload
        if (request.PassportImage == null || request.PassportImage.Length == 0)
        {
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "Passport image is required for companion validation" };
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "travora_ocr");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid()}{Path.GetExtension(request.PassportImage.FileName)}");

        string imageUrl = "https://res.cloudinary.com/travora/image/upload/vdefault/companion.jpg";
        bool passportVerified = false;
        string finalPassportNumber = string.Empty;
        string? ocrResultJson = null;

        // OCR-extracted fields (source of truth for personal data)
        string ocrFirstName = string.Empty;
        string ocrLastName = string.Empty;
        string ocrNationality = string.Empty;
        DateTime? ocrDob = null;
        DateTime? ocrExpiry = null;

        try
        {
            await using (var fileStream = System.IO.File.Create(tempPath))
            {
                await request.PassportImage.CopyToAsync(fileStream, cancellationToken);
            }

            await using (var uploadStream = System.IO.File.OpenRead(tempPath))
            {
                var uploadResult = await _cloudinaryService.UploadFileAsync(
                    uploadStream, request.PassportImage.FileName, "travora/companions");
                if (!string.IsNullOrEmpty(uploadResult))
                    imageUrl = uploadResult;
            }

            var ocrResult = await _ocrService.ExtractPassportDataAsync(tempPath);

            // Validate OCR result using shared helper (checks score >= 90, ValidExpirationDate, expiry, ValidNumber)
            var (isOcrValid, ocrError) = PassportOcrValidationHelper.ValidateCompanionPassport(ocrResult);
            if (!isOcrValid)
                return new ValidateCompanionResponse { IsValid = false, ErrorMessage = ocrError };

            passportVerified = true;
            finalPassportNumber = ocrResult!.Number ?? string.Empty;
            ocrFirstName = ocrResult.Names ?? string.Empty;
            ocrLastName = ocrResult.Surname ?? string.Empty;
            ocrNationality = ocrResult.Nationality ?? string.Empty;

            if (DateTime.TryParse(ocrResult.DateOfBirthFormatted, out var parsedDob))
                ocrDob = parsedDob;
            if (DateTime.TryParse(ocrResult.ExpirationDateFormatted, out var parsedExpiry))
                ocrExpiry = parsedExpiry;

            ocrResultJson = System.Text.Json.JsonSerializer.Serialize(ocrResult);
        }
        catch (Exception ex)
        {
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = $"OCR Verification failed: {ex.Message}" };
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }

        if (string.IsNullOrWhiteSpace(finalPassportNumber))
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "Could not extract passport number from image. Please try again with a clearer photo." };


        if (finalPassportNumber == draft.PassengerInfo?.PassportNumber)
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "You cannot add yourself as a companion" };

        // 3. Validate companion ticket with airline API
        var airlineReq = new AirlineValidateTicketRequest
        {
            PassportNumber = finalPassportNumber,
            TicketNumber = request.TicketNumber,
            FlightNumber = draft.FlightInfo.FlightNumber,
            FlightDate = draft.FlightInfo.FlightDate ?? string.Empty
        };

        var airlineRes = await _airlineService.ValidateTicketAsync(airlineReq, cancellationToken);
        var flightData = airlineRes.Flight ?? airlineRes.Ticket?.Flight ?? airlineRes.FlightInfo;
        var passengerData = airlineRes.Passenger ?? airlineRes.Ticket?.Passenger ?? airlineRes.PassengerInfo;

        if (!airlineRes.IsValid || flightData == null || passengerData == null)
        {
            var errorMsg = airlineRes.Errors != null && airlineRes.Errors.Any()
                ? string.Join(", ", airlineRes.Errors)
                : "Invalid ticket details for this companion.";
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = errorMsg };
        }

        // 4. Ensure the companion is on the same flight
        if (flightData.FlightNumber != draft.FlightInfo.FlightNumber)
        {
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "The companion is not on the same flight" };
        }

        // 5. Save to draft — OCR data takes priority over Airline data
        var newCompanion = new DraftCompanion
        {
            FirstName = !string.IsNullOrWhiteSpace(ocrFirstName) ? ocrFirstName : (passengerData.FirstName ?? string.Empty),
            LastName = !string.IsNullOrWhiteSpace(ocrLastName) ? ocrLastName : (passengerData.LastName ?? string.Empty),
            PassportNumber = finalPassportNumber,
            TicketNumber = request.TicketNumber,
            SeatNumber = airlineRes.Ticket?.SeatNumber ?? passengerData.SeatNumber ?? string.Empty,
            PassportImageUrl = imageUrl,
            Nationality = !string.IsNullOrWhiteSpace(ocrNationality) ? ocrNationality : passengerData.Nationality,
            DateOfBirth = ocrDob ?? (DateTime.TryParse(passengerData.DateOfBirth, out var dob) ? dob : null),
            PassportExpiryDate = ocrExpiry ?? (DateTime.TryParse(passengerData.PassportExpiryDate, out var expiry) ? expiry : null),
            IsVerified = passportVerified,
            PassportFileSizeKb = (int)(request.PassportImage.Length / 1024),
            PassportMimeType = request.PassportImage.ContentType,
            PassportOcrResultJson = ocrResultJson
        };

        // Ensure we don't add the same companion twice (by passport)
        draft.Companions.RemoveAll(c => c.PassportNumber == finalPassportNumber);
        draft.Companions.Add(newCompanion);
        await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new ValidateCompanionResponse
        {
            IsValid = true,
            Companion = new CompanionDetails
            {
                FirstName = newCompanion.FirstName,
                LastName = newCompanion.LastName,
                SeatNumber = newCompanion.SeatNumber,
                TravelClass = airlineRes.Ticket?.TravelClass ?? passengerData.TravelClass ?? "Economy",
                PassportNumber = newCompanion.PassportNumber,
                PassportImageUrl = newCompanion.PassportImageUrl,
                Nationality = newCompanion.Nationality,
                DateOfBirth = newCompanion.DateOfBirth,
                PassportExpiryDate = newCompanion.PassportExpiryDate
            },
            TotalCompanions = draft.Companions.Count
        };
    }

    public async Task<DoorToDoorValidateBaggageResponse> ValidateBaggageAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new DoorToDoorValidateBaggageResponse { IsValid = false, ErrorMessage = "Draft order not found" };

        var allTicketNumbers = new List<string> { draft.TicketNumber };
        allTicketNumbers.AddRange(draft.Companions.Select(c => c.TicketNumber));

        // 1. Baggage Allowance Check
        var allowanceTasks = allTicketNumbers.Select(tn => new
        {
            TicketNumber = tn,
            Task = _airlineService.GetBaggageAllowanceAsync(tn, cancellationToken)
        }).ToList();

        await Task.WhenAll(allowanceTasks.Select(t => t.Task));
        int summedAllowance = allowanceTasks.Sum(t => t.Task.Result.AllowedBaggageCount);

        // 2. Compare input vs Allowance
        if (draft.BaggageCount > summedAllowance)
        {
            return new DoorToDoorValidateBaggageResponse
            {
                IsValid = false,
                ErrorMessage = "The total number of bags entered exceeds the baggage allowance limit for these tickets"
            };
        }

        draft.TotalBaggageCount = draft.BaggageCount;
        draft.BaggageValidated = true;

        // 3. Distribute Bags among Main Passenger and Companions for invoice breakdown
        int remainingBags = draft.BaggageCount;
        var primaryAllowance = allowanceTasks.First(t => t.TicketNumber == draft.TicketNumber).Task.Result.AllowedBaggageCount;
        int primaryBags = Math.Min(remainingBags, primaryAllowance);
        remainingBags -= primaryBags;

        var breakdown = new List<BaggageBreakdown>
        {
            new BaggageBreakdown { TicketNumber = draft.TicketNumber, BaggageCount = primaryBags }
        };

        foreach (var comp in draft.Companions)
        {
            var compAllowance = allowanceTasks.FirstOrDefault(t => t.TicketNumber == comp.TicketNumber)?.Task.Result.AllowedBaggageCount ?? 0;
            int compBags = Math.Min(remainingBags, compAllowance);
            comp.BaggageCount = compBags;
            remainingBags -= compBags;

            breakdown.Add(new BaggageBreakdown { TicketNumber = comp.TicketNumber, BaggageCount = compBags });
        }

        if (remainingBags > 0)
        {
            breakdown[0].BaggageCount += remainingBags;
        }

        await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new DoorToDoorValidateBaggageResponse
        {
            IsValid = true
        };
    }

    public async Task<ResolveLocationResponse> ResolveLocationAsync(int customerId, ResolveLocationRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Session not found" };

        if (string.Equals(request.LocationType, "delivery", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(draft.PickupFormattedAddress))
                return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Pickup location must be specified first" };
        }
        else // pickup
        {
            if (!draft.BaggageValidated)
                return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Baggage validation must be completed first" };
        }

        var result = await _geocodingService.ReverseGeocodeAsync(request.Latitude, request.Longitude, cancellationToken);
        
        var response = new ResolveLocationResponse
        {
            IsValid = true,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            FormattedAddress = result?.FormattedAddress ?? string.Empty,
            StreetAddress = result?.StreetAddress,
            City = result?.City,
            State = result?.State,
            Country = result?.Country,
            PostalCode = result?.PostalCode,
            LocationType = request.LocationType
        };

        if (string.Equals(request.LocationType, "delivery", StringComparison.OrdinalIgnoreCase))
        {
                draft.DeliveryLatitude = request.Latitude;
                draft.DeliveryLongitude = request.Longitude;
                draft.DeliveryFormattedAddress = response.FormattedAddress;
                draft.DeliveryStreetAddress = result?.StreetAddress;
                draft.DeliveryCity = result?.City;
                draft.DeliveryState = result?.State;
                draft.DeliveryCountry = result?.Country;
                draft.DeliveryPostalCode = result?.PostalCode;
            }
            else
            {
                draft.PickupLatitude = request.Latitude;
                draft.PickupLongitude = request.Longitude;
                draft.PickupFormattedAddress = response.FormattedAddress;
                draft.PickupStreetAddress = result?.StreetAddress;
                draft.PickupCity = result?.City;
                draft.PickupState = result?.State;
                draft.PickupCountry = result?.Country;
                draft.PickupPostalCode = result?.PostalCode;
            }
            await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return response;
    }

    // ===================================================================
    // STEP 3.5 — Update Location (Manual Correction)
    // ===================================================================
    public async Task<ResolveLocationResponse> UpdateLocationAsync(
        int customerId, UpdateLocationRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Session not found" };

        bool isDelivery = string.Equals(request.LocationType, "delivery", StringComparison.OrdinalIgnoreCase);

        if (isDelivery)
        {
            if (draft.DeliveryFormattedAddress == null)
                return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Delivery location must be resolved first before updating" };

            if (request.StreetAddress != null) draft.DeliveryStreetAddress = request.StreetAddress;
            if (request.City != null) draft.DeliveryCity = request.City;
            if (request.State != null) draft.DeliveryState = request.State;
            if (request.Country != null) draft.DeliveryCountry = request.Country;
            if (request.PostalCode != null) draft.DeliveryPostalCode = request.PostalCode;
        }
        else
        {
            if (draft.PickupFormattedAddress == null)
                return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Pickup location must be resolved first before updating" };

            if (request.StreetAddress != null) draft.PickupStreetAddress = request.StreetAddress;
            if (request.City != null) draft.PickupCity = request.City;
            if (request.State != null) draft.PickupState = request.State;
            if (request.Country != null) draft.PickupCountry = request.Country;
            if (request.PostalCode != null) draft.PickupPostalCode = request.PostalCode;
        }

        await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new ResolveLocationResponse
        {
            IsValid = true,
            Latitude = isDelivery ? (draft.DeliveryLatitude ?? 0) : (draft.PickupLatitude ?? 0),
            Longitude = isDelivery ? (draft.DeliveryLongitude ?? 0) : (draft.PickupLongitude ?? 0),
            FormattedAddress = isDelivery ? (draft.DeliveryFormattedAddress ?? string.Empty) : (draft.PickupFormattedAddress ?? string.Empty),
            StreetAddress = isDelivery ? draft.DeliveryStreetAddress : draft.PickupStreetAddress,
            City = isDelivery ? draft.DeliveryCity : draft.PickupCity,
            State = isDelivery ? draft.DeliveryState : draft.PickupState,
            Country = isDelivery ? draft.DeliveryCountry : draft.PickupCountry,
            PostalCode = isDelivery ? draft.DeliveryPostalCode : draft.PickupPostalCode,
            LocationType = request.LocationType
        };
    }

    public async Task<AvailableDatesResponse> GetAvailablePickupDatesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Session not found" };

        if (string.IsNullOrEmpty(draft.PickupFormattedAddress))
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Pickup location must be specified first" };

        if (string.IsNullOrEmpty(draft.DeliveryFormattedAddress))
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Delivery location must be specified first" };

        var departure = draft.FlightInfo.DepartureTimeUtc;
        var earliestPossible = departure.AddDays(-4);
        var latestPossible = departure.AddHours(-12);

        if (DateTime.UtcNow >= latestPossible)
        {
            return new AvailableDatesResponse
            {
                IsValid = false,
                ErrorMessage = "Booking deadline has passed. You must book at least 12 hours before departure."
            };
        }

        var availableDates = new List<DateTime>();
        var today = DateTime.UtcNow.Date;
        var startPoint = earliestPossible.Date < today ? today : earliestPossible.Date;

        for (var day = startPoint; day <= latestPossible.Date; day = day.AddDays(1))
        {
            availableDates.Add(day);
        }

        return new AvailableDatesResponse
        {
            IsValid = true,
            AvailableDates = availableDates
        };
    }

    public async Task<AvailableSlotsResponse> GetAvailableSlotsAsync(int customerId, DateTime date, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Draft order not found. Please start from Step 1." };

        if (string.IsNullOrEmpty(draft.PickupFormattedAddress))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Pickup location must be specified first" };

        if (string.IsNullOrEmpty(draft.DeliveryFormattedAddress))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Delivery location must be specified first" };

        var earliestPossible = draft.FlightInfo.DepartureTimeUtc.AddDays(-4);
        var latestPossible = draft.FlightInfo.DepartureTimeUtc.AddHours(-12);
        var today = DateTime.UtcNow.Date;

        if (date.Date < today)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Cannot select a day in the past" };

        if (date.Date < earliestPossible.Date || date.Date > latestPossible.Date)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = $"Execution date must be between {earliestPossible:yyyy-MM-dd} and {latestPossible:yyyy-MM-dd}" };

        var response = new AvailableSlotsResponse { IsValid = true };
        DateTime? absoluteCutoffUtc = latestPossible;

        if (date.Date == latestPossible.Date)
        {
            response.CutoffTime = latestPossible.ToString(@"HH:mm");
            response.Note = $"The last available slot must end before {response.CutoffTime}";
        }

        var allDrivers = await _context.Employees
            .Include(e => e.Vehicle)
            .Where(e => e.JobRole == Domain.Enums.JobRole.Driver 
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

            var slotEndUtc = date.Date.Add(end);
            bool isAvailable = true;

            // Skip slots that have already passed today
            if (date.Date == DateTime.UtcNow.Date && start < DateTime.UtcNow.TimeOfDay)
            {
                isAvailable = false;
            }
            else if (absoluteCutoffUtc.HasValue && slotEndUtc > absoluteCutoffUtc.Value)
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
                {
                    isAvailable = false;
                }
            }

            response.AvailableSlots.Add(new SlotItem
            {
                Slot = slot,
                Available = isAvailable
            });
        }

        response.AvailableSlots = response.AvailableSlots
            .Where(s => s.Available)
            .ToList();

        return response;
    }

    private bool IsShiftCovering(Domain.Enums.ShiftType shift, TimeSpan slotStart, TimeSpan slotEnd)
    {
        return shift switch
        {
            Domain.Enums.ShiftType.Morning => slotStart >= TimeSpan.FromHours(8) && slotEnd <= TimeSpan.FromHours(16),
            Domain.Enums.ShiftType.Evening => slotStart >= TimeSpan.FromHours(16) && slotEnd <= TimeSpan.FromHours(24),
            Domain.Enums.ShiftType.Night => slotStart >= TimeSpan.Zero && slotEnd <= TimeSpan.FromHours(8),
            Domain.Enums.ShiftType.rotating => true,
            _ => false
        };
    }

    private bool HasConflict(Domain.Entities.Employee driver, DateTime date, TimeSpan slotStart, TimeSpan slotEnd)
    {
        // Check if the driver has any order service overlapping this slot on the given date
        return driver.AssignedOrderServices.Any(os => 
            os.ScheduledStartTime.Date == date &&
            os.ScheduledStartTime.TimeOfDay < slotEnd &&
            os.ScheduledEndTime.TimeOfDay > slotStart
        );
    }

    public async Task<SetCustomsTypeResponse> SetCustomsTypeAsync(int customerId, SetCustomsTypeRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            return new SetCustomsTypeResponse { Success = false, ErrorMessage = "No active draft order found." };

        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new SetCustomsTypeResponse { Success = false, ErrorMessage = "Pickup slot must be selected first" };

        if (string.IsNullOrEmpty(draft.SelectedDeliverySlot))
            return new SetCustomsTypeResponse { Success = false, ErrorMessage = "Delivery slot must be selected first" };

        string normalizedType = request.CustomsType?.Trim().ToLower().Replace("_", "").Replace(" ", "") ?? "";
        
        if (normalizedType == "greenfield")
        {
            draft.CustomsType = "GreenField";
            await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);
            return new SetCustomsTypeResponse 
            { 
                Success = true, 
                CustomsType = "GreenField", 
                Message = "Green line selected, no customs fees apply" 
            };
        }
        else if (normalizedType == "redfield")
        {
            draft.CustomsType = "RedField";
            await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);
            return new SetCustomsTypeResponse 
            { 
                Success = true, 
                CustomsType = "RedField", 
                Message = "Red line selected, please add customs items" 
            };
        }
        else
        {
            return new SetCustomsTypeResponse { Success = false, ErrorMessage = "Invalid customs type" };
        }
    }

    public async Task<List<CustomsCategoryDto>> GetCustomsCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var externalCategories = await _airlineService.GetCustomsCategoriesAsync(cancellationToken);
        return externalCategories.Select(c => new CustomsCategoryDto
        {
            CategoryId = c.CategoryId,
            Name = c.Name
        }).ToList();
    }

    public async Task<CustomsLookupResponse> LookupCustomsProductAsync(string productName, CancellationToken cancellationToken = default)
    {
        var result = await _airlineService.LookupCustomsProductAsync(productName, cancellationToken);
        if (!result.Found || result.Product == null)
            return new CustomsLookupResponse { Found = false, Message = "Product not found, please enter details manually" };

        return new CustomsLookupResponse
        {
            Found = true,
            ProductName = result.Product.Name,
            CustomsRatePercentage = result.Product.CustomsRate,
            Category = result.Product.Category
        };
    }

    public async Task<AddCustomsItemResponse> AddCustomsItemAsync(int customerId, AddCustomsItemRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            return new AddCustomsItemResponse { Success = false, ErrorMessage = "No active draft order found." };

        if (string.IsNullOrEmpty(draft.CustomsType))
            return new AddCustomsItemResponse { Success = false, ErrorMessage = "Customs type must be specified first" };

        if (draft.CustomsType != "RedField")
            return new AddCustomsItemResponse { Success = false, ErrorMessage = "Cannot add items to the Green line" };

        // Get the rate automatically from the CategoryName + ItemDescription
        var lookupResult = await _airlineService.GetCustomsRateAsync(request.ExternalCategoryName, request.ItemDescription, cancellationToken);
        decimal customsRate = lookupResult.Found && lookupResult.Product != null
            ? lookupResult.Product.CustomsRate
            : 0m;

        int uploadedCount = (request.PurchaseInvoice != null && request.PurchaseInvoice.Length > 0 ? 1 : 0) +
                            (request.PurchaseInvoices != null ? request.PurchaseInvoices.Count(f => f.Length > 0) : 0);

        if (request.Quantity <= 1 && uploadedCount > 1)
        {
            return new AddCustomsItemResponse
            {
                Success = false,
                ErrorMessage = "Cannot upload multiple invoices when the item quantity is 1 or less."
            };
        }

        List<string> invoiceUrls = new List<string>();
        if (request.PurchaseInvoice != null && request.PurchaseInvoice.Length > 0)
        {
            using var stream = request.PurchaseInvoice.OpenReadStream();
            var uploadResult = await _cloudinaryService.UploadFileAsync(stream, request.PurchaseInvoice.FileName, "travora/customs-invoices");
            if (!string.IsNullOrEmpty(uploadResult))
                invoiceUrls.Add(uploadResult);
        }

        if (request.PurchaseInvoices != null && request.PurchaseInvoices.Any())
        {
            foreach (var file in request.PurchaseInvoices)
            {
                if (file.Length > 0)
                {
                    using var stream = file.OpenReadStream();
                    var uploadResult = await _cloudinaryService.UploadFileAsync(stream, file.FileName, "travora/customs-invoices");
                    if (!string.IsNullOrEmpty(uploadResult))
                        invoiceUrls.Add(uploadResult);
                }
            }
        }

        var item = new DraftCustomsItem
        {
            ItemDescription = request.ItemDescription,
            ItemType = lookupResult.Found && lookupResult.Product != null ? lookupResult.Product.Category ?? "Other" : "Other",
            DeclaredValue = request.DeclaredValue,
            Quantity = request.Quantity,
            CustomsRatePercentage = customsRate,
            PurchaseInvoiceUrls = invoiceUrls,
            ExternalCategoryId = request.ExternalCategoryId,
            ExternalCategoryName = request.ExternalCategoryName
        };

        draft.CustomsItems.Add(item);
        await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new AddCustomsItemResponse
        {
            Success = true,
            AddedItem = item,
            TotalDeclaredValue = draft.CustomsItems.Sum(x => x.TotalValue),
            TotalCustomsFee = draft.TotalCustomsFee
        };
    }

    public async Task<InvoiceResponse> GetInvoiceAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            return new InvoiceResponse { IsValid = false, ErrorMessage = "Draft order not found." };

        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new InvoiceResponse { IsValid = false, ErrorMessage = "Pickup slot must be selected first" };

        if (string.IsNullOrEmpty(draft.SelectedDeliverySlot))
            return new InvoiceResponse { IsValid = false, ErrorMessage = "Delivery slot must be selected first" };

        var pkg = await _context.Packages.FirstOrDefaultAsync(p => p.PackageCode == PackageCodes.DoorToDoor, cancellationToken);
        
        // If no package found, fallback to defaults based on spec to prevent crash
        decimal basePrice = pkg?.TotalBasePrice ?? 80m;
        decimal discountAmount = pkg != null ? (pkg.TotalBasePrice * (pkg.Discount ?? 0) / 100) : 0m;

        int incBags = pkg?.IncludedBaggageCount ?? 2;
        decimal extraBagPrice = pkg?.ExtraBaggagePrice ?? 25m;
        int incComps = pkg?.IncludedCompanionsCount ?? 1;
        decimal extraCompPrice = pkg?.ExtraCompanionPrice ?? 20m;
        decimal discount = pkg?.Discount ?? 0m;

        int extraBags = Math.Max(0, draft.TotalBaggageCount - incBags);
        decimal extraBagFee = extraBags * extraBagPrice;

        int totalCompanions = draft.Companions.Count;
        int extraComps = Math.Max(0, totalCompanions - incComps);
        decimal extraCompFee = extraComps * extraCompPrice;

        decimal customsValue = draft.CustomsItems.Sum(x => x.TotalValue);
        decimal customsFee = draft.TotalCustomsFee;

        decimal subtotal = basePrice + extraBagFee + extraCompFee + customsFee + customsValue;
        decimal taxAmount = subtotal * 0m;
        decimal totalAmount = subtotal - discountAmount + taxAmount;

        return new InvoiceResponse
        {
            IsValid = true,
            InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-{new Random().Next(1000, 9999)}",
            Breakdown = new InvoiceBreakdown
            {
                PackageValue = basePrice,
                BaggageDetails = new BaggageDetails
                {
                    IncludedBags = incBags,
                    TotalBags = draft.TotalBaggageCount,
                    ExtraBags = extraBags,
                    ExtraBaggageFee = extraBagFee
                },
                CompanionDetails = new CompanionDetailsInvoice
                {
                    IncludedCompanions = incComps,
                    TotalCompanions = totalCompanions,
                    ExtraCompanions = extraComps,
                    ExtraCompanionsFee = extraCompFee
                },
                CustomsValue = customsValue,
                CustomsFee = customsFee,
                Subtotal = subtotal,
                TaxAmount = Math.Round(taxAmount, 2),
                Discount = Math.Round(discountAmount, 2),
                TotalAmount = Math.Round(totalAmount, 2)
            }
        };
    }

    public async Task<ConfirmOrderResponse> ConfirmOrderAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Draft order not found" };

        if (!draft.BaggageValidated)
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Baggage validation must be completed first" };
        if (string.IsNullOrEmpty(draft.PickupFormattedAddress))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Pickup location must be specified first" };
        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Pickup slot must be selected first" };
        if (string.IsNullOrEmpty(draft.DeliveryFormattedAddress))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Delivery location must be specified first" };
        if (string.IsNullOrEmpty(draft.SelectedDeliverySlot))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Delivery slot must be selected first" };

        var strategy = _context.Database.CreateExecutionStrategy();
        
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var invoiceDto = await GetInvoiceAsync(customerId, cancellationToken);
                var pkg = await _context.Packages.FirstOrDefaultAsync(p => 
                    p.PackageCode == PackageCodes.DoorToDoor, cancellationToken);

                string flightNo = draft.FlightInfo.FlightNumber;

                // 1) Extract IATA codes from draft
                var depIata = (draft.FlightInfo.DepartureIataCode
                    ?? draft.FlightInfo.DepartureAirport
                    ?? "").Trim();
                var arrIata = (draft.FlightInfo.ArrivalIataCode
                    ?? draft.FlightInfo.ArrivalAirport
                    ?? "").Trim();

                // 2) Find flight by FlightNumber
                var flight = await _context.Flights
                    .FirstOrDefaultAsync(f => f.FlightNumber == flightNo, cancellationToken);

                // 3) If new → create it
                if (flight == null)
                {
                    flight = new Domain.Entities.Flight
                    {
                        FlightNumber = flightNo,
                        AirlineIcaoCode = (draft.FlightInfo.AirlineIcaoCode ?? "MS").Trim(),
                        AirlineName = draft.FlightInfo.AirlineName ?? string.Empty,
                        DepartureIataCode = depIata,
                        ArrivalIataCode = arrIata,
                        DepartureTerminal = draft.FlightInfo.Terminal,
                        DepartureGate = draft.FlightInfo.Gate,
                        ScheduledDepartureTime = draft.FlightInfo.DepartureTimeUtc,
                        ScheduledArrivalTime = draft.FlightInfo.ArrivalTimeUtc
                            ?? draft.FlightInfo.DepartureTimeUtc.AddHours(4),
                        FlightStatus = Domain.Enums.FlightStatus.Scheduled,
                        DataSource = "AirlineSimulation"
                    };
                    _context.Flights.Add(flight);
                }
                // 4) If exists → update data
                else
                {
                    flight.DepartureIataCode = depIata;
                    flight.ArrivalIataCode = arrIata;
                    flight.DepartureTerminal = draft.FlightInfo.Terminal ?? flight.DepartureTerminal;
                    flight.DepartureGate = draft.FlightInfo.Gate ?? flight.DepartureGate;
                    flight.ScheduledDepartureTime = draft.FlightInfo.DepartureTimeUtc;
                    flight.ScheduledArrivalTime = draft.FlightInfo.ArrivalTimeUtc
                        ?? draft.FlightInfo.DepartureTimeUtc.AddHours(4);
                    flight.UpdatedAt = DateTime.UtcNow;
                }

                // 5) Link to Airport from Airports table
                var departureAirport = await _context.Airports
                    .FirstOrDefaultAsync(a => a.CodeIataAirport == depIata, cancellationToken);
                if (departureAirport != null)
                    flight.DepartureAirportId = departureAirport.AirportId;

                var arrivalAirport = await _context.Airports
                    .FirstOrDefaultAsync(a => a.CodeIataAirport == arrIata, cancellationToken);
                if (arrivalAirport != null)
                    flight.ArrivalAirportId = arrivalAirport.AirportId;

                // 6) Save
                await _context.SaveChangesAsync(cancellationToken);

                var pickupLocation = new Domain.Entities.Location
                {
                    StreetAddress = draft.PickupStreetAddress ?? draft.PickupFormattedAddress ?? string.Empty,
                    City = draft.PickupCity ?? string.Empty,
                    State = draft.PickupState ?? string.Empty,
                    Country = draft.PickupCountry ?? string.Empty,
                    PostalCode = draft.PickupPostalCode ?? string.Empty,
                    GpsLatitude = (decimal)(draft.PickupLatitude ?? 0),
                    GpsLongitude = (decimal)(draft.PickupLongitude ?? 0),
                    LocationType = Domain.Enums.LocationType.Pickup,
                    CustomerId = customerId
                };
                _context.Locations.Add(pickupLocation);

                var deliveryLocation = new Domain.Entities.Location
                {
                    StreetAddress = draft.DeliveryStreetAddress ?? draft.DeliveryFormattedAddress ?? string.Empty,
                    City = draft.DeliveryCity ?? string.Empty,
                    State = draft.DeliveryState ?? string.Empty,
                    Country = draft.DeliveryCountry ?? string.Empty,
                    PostalCode = draft.DeliveryPostalCode ?? string.Empty,
                    GpsLatitude = (decimal)(draft.DeliveryLatitude ?? 0),
                    GpsLongitude = (decimal)(draft.DeliveryLongitude ?? 0),
                    LocationType = Domain.Enums.LocationType.Delivery,
                    CustomerId = customerId
                };
                _context.Locations.Add(deliveryLocation);
                await _context.SaveChangesAsync(cancellationToken);

                int pickupId = pickupLocation.LocationId;
                int deliveryId = deliveryLocation.LocationId;

                var order = new Domain.Entities.Order
                {
                    CustomerId = customerId,
                    FlightId = flight.FlightId,
                    PackageId = pkg?.PackageId ?? 1,
                    PickupLocationId = pickupId,
                    DeliveryLocationId = deliveryId,
                    OrderStatus = Domain.Enums.OrderStatus.Pending,
                    TicketNumber = draft.TicketNumber,
                    ExtraCompanionsCount = invoiceDto.Breakdown.CompanionDetails.ExtraCompanions,
                    ExtraCompanionsFee = invoiceDto.Breakdown.CompanionDetails.ExtraCompanionsFee,
                    TotalBaggageCount = invoiceDto.Breakdown.BaggageDetails.TotalBags,
                    ExtraBaggageCount = invoiceDto.Breakdown.BaggageDetails.ExtraBags,
                    ExtraBaggageFee = invoiceDto.Breakdown.BaggageDetails.ExtraBaggageFee,
                    TotalAmount = invoiceDto.Breakdown.TotalAmount,
                    PickupDate = draft.SelectedSlotDate ?? draft.FlightInfo.DepartureTimeUtc.AddHours(-12),
                    PickupTimeSlot = draft.SelectedSlot ?? "10:00-12:00",
                    DeliveryDate = draft.SelectedDeliverySlotDate ?? draft.FlightInfo.ArrivalTimeUtc?.AddDays(1) ?? draft.FlightInfo.DepartureTimeUtc.AddDays(1),
                    DeliveryTimeSlot = draft.SelectedDeliverySlot ?? "10:00-12:00"
                };

                _context.Orders.Add(order);
                await _context.SaveChangesAsync(cancellationToken);

                var invoice = new Domain.Entities.Invoice
                {
                    InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-{new Random().Next(1000, 9999)}",
                    OrderId = order.OrderId,
                    PackageFee = invoiceDto.Breakdown.PackageValue,
                    CustomsFee = invoiceDto.Breakdown.CustomsFee,
                    Subtotal = invoiceDto.Breakdown.Subtotal,
                    TaxAmount = invoiceDto.Breakdown.TaxAmount,
                    TotalAmount = invoiceDto.Breakdown.TotalAmount,
                    InvoiceStatus = Domain.Enums.InvoiceStatus.Pending,
                    DueDate = DateTime.UtcNow
                };
                _context.Invoices.Add(invoice);

                // ===== Companions with extra fields =====
                var companionIdMap = new Dictionary<string, int>();

                foreach (var comp in draft.Companions)
                {
                    var companionEntity = await _context.Companions
                        .FirstOrDefaultAsync(c => c.PassportNumber == comp.PassportNumber, cancellationToken);
                    if (companionEntity == null)
                    {
                        companionEntity = new Domain.Entities.Companion
                        {
                            Firstname = comp.FirstName,
                            Lastname = comp.LastName,
                            PassportNumber = comp.PassportNumber,
                            Nationality = comp.Nationality,
                            DateOfBirth = comp.DateOfBirth,
                            PassportExpiryDate = comp.PassportExpiryDate
                        };
                        _context.Companions.Add(companionEntity);
                    }
                    else
                    {
                        companionEntity.Firstname = string.IsNullOrEmpty(comp.FirstName) ? companionEntity.Firstname : comp.FirstName;
                        companionEntity.Lastname = string.IsNullOrEmpty(comp.LastName) ? companionEntity.Lastname : comp.LastName;
                        companionEntity.Nationality = comp.Nationality ?? companionEntity.Nationality;
                        companionEntity.DateOfBirth = comp.DateOfBirth ?? companionEntity.DateOfBirth;
                        companionEntity.PassportExpiryDate = comp.PassportExpiryDate ?? companionEntity.PassportExpiryDate;
                    }
                    await _context.SaveChangesAsync(cancellationToken);
                    companionIdMap[comp.PassportNumber] = companionEntity.CompanionId;

                    // Save document & OCR validation in DB just like CarService
                    var document = new Domain.Entities.Document
                    {
                        OwnerId = companionEntity.CompanionId,
                        OwnerType = DocumentOwnerType.Companion,
                        DocumentType = DocumentType.Passport,
                        FilePath = comp.PassportImageUrl,
                        FileSizeKb = comp.PassportFileSizeKb > 0 ? comp.PassportFileSizeKb : 0,
                        MimeType = !string.IsNullOrEmpty(comp.PassportMimeType) ? comp.PassportMimeType : "image/jpeg",
                        VerificationStatus = comp.IsVerified ? VerificationStatus.Approved : VerificationStatus.UnderReview,
                        UploadedAt = DateTime.UtcNow,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Documents.Add(document);
                    await _context.SaveChangesAsync(cancellationToken);

                    if (!string.IsNullOrEmpty(comp.PassportOcrResultJson))
                    {
                        try
                        {
                            var ocrResult = JsonSerializer.Deserialize<PassportOcrResult>(
                                comp.PassportOcrResultJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            
                            if (ocrResult != null)
                            {
                                DateTime.TryParse(ocrResult.DateOfBirthFormatted, out var extractedDob);
                                DateTime.TryParse(ocrResult.ExpirationDateFormatted, out var extractedExpiry);

                                var validation = new PassportValidation
                                {
                                    DocumentId = document.DocumentId,
                                    ExpiryCheckPassed = comp.PassportExpiryDate > DateTime.UtcNow,
                                    FormatCheckPassed = ocrResult.CustomValidComposite ?? false,
                                    NameMatchCheck = string.Equals(ocrResult.Surname, comp.LastName, StringComparison.OrdinalIgnoreCase),
                                    BirthDateMatchCheck = extractedDob.Date == (comp.DateOfBirth?.Date ?? DateTime.MinValue.Date),
                                    ValidationStatus = comp.IsVerified ? PassportValidationStatus.Passed : PassportValidationStatus.RequiresManualReview,
                                    OcrConfidenceScore = ocrResult.ValidScore / 100.0m,
                                    ManualReviewRequired = !comp.IsVerified,
                                    MrzType = ocrResult.MrzType,
                                    RawMrzText = ocrResult.RawText,
                                    ValidScore = ocrResult.ValidScore,
                                    MrzMethod = ocrResult.Method,
                                    CheckNumber = ocrResult.CheckNumber,
                                    CheckDateOfBirth = ocrResult.CheckDateOfBirth,
                                    CheckExpirationDate = ocrResult.CheckExpirationDate,
                                    CheckComposite = ocrResult.CheckComposite,
                                    ValidNumber = ocrResult.ValidNumber,
                                    ValidDateOfBirth = ocrResult.ValidDateOfBirth,
                                    ValidExpirationDate = ocrResult.ValidExpirationDate,
                                    ValidComposite = ocrResult.CustomValidComposite,
                                    ExtractedPassportNumber = ocrResult.Number,
                                    ExtractedSurname = ocrResult.Surname,
                                    ExtractedGivenNames = ocrResult.Names,
                                    ExtractedNationality = ocrResult.Nationality,
                                    ExtractedDateOfBirth = extractedDob != default ? extractedDob : null,
                                    ExtractedExpiryDate = extractedExpiry != default ? extractedExpiry : null,
                                    ExtractedGender = ocrResult.SexFormatted,
                                    ValidatedAt = DateTime.UtcNow,
                                    CreatedAt = DateTime.UtcNow
                                };

                                _context.Set<PassportValidation>().Add(validation);
                            }
                        }
                        catch { /* Prevent failures in OCR logging from failing the entire checkout */ }
                    }

                    _context.OrderCompanions.Add(new Domain.Entities.OrderCompanion
                    {
                        OrderId = order.OrderId,
                        CompanionId = companionEntity.CompanionId,
                        TicketNumber = comp.TicketNumber
                    });
                }

                // ===== Baggages — Customer + Companion =====
                var primaryBaggageCount = draft.TotalBaggageCount - draft.Companions.Sum(c => c.BaggageCount);
                for (int i = 0; i < primaryBaggageCount; i++)
                {
                    _context.Baggages.Add(new Domain.Entities.Baggage
                    {
                        OrderId = order.OrderId,
                        CustomerId = customerId,
                        OwnerType = Domain.Enums.BaggageOwnerType.Customer
                    });
                }

                foreach (var comp in draft.Companions)
                {
                    if (companionIdMap.TryGetValue(comp.PassportNumber, out int compId))
                    {
                        for (int i = 0; i < comp.BaggageCount; i++)
                        {
                            _context.Baggages.Add(new Domain.Entities.Baggage
                            {
                                OrderId = order.OrderId,
                                CustomerId = customerId,
                                CompanionId = compId,
                                OwnerType = Domain.Enums.BaggageOwnerType.Companion
                            });
                        }
                    }
                }

                // ===== Customs — ItemType, TotalValue, TotalCustomsFee =====
                if (draft.CustomsType == "RedField" && draft.CustomsItems.Any())
                {
                    var declaration = new Domain.Entities.CustomsDeclaration
                    {
                        OrderId = order.OrderId,
                        CustomsType = Domain.Enums.CustomsType.RedField,
                        TotalDeclaredValue = draft.CustomsItems.Sum(x => x.TotalValue),
                        TotalCustomsFee = draft.CustomsItems.Sum(x => x.TotalCustomsValue)
                    };
                    _context.CustomsDeclarations.Add(declaration);
                    await _context.SaveChangesAsync(cancellationToken);

                    foreach (var item in draft.CustomsItems)
                    {
                        // Parse ItemType enum from string
                        if (!Enum.TryParse<Domain.Enums.ItemType>(item.ItemType, true, out var parsedItemType))
                            parsedItemType = Domain.Enums.ItemType.Other;

                        var customsItem = new Domain.Entities.CustomsItem
                        {
                            CustomsId = declaration.CustomsId,
                            ItemDescription = item.ItemDescription,
                            ItemType = parsedItemType,
                            DeclaredValue = item.DeclaredValue,
                            Quantity = item.Quantity,
                            TotalValue = item.TotalValue,
                            CustomsRatePercentage = item.CustomsRatePercentage,
                            TotalCustomsValue = item.TotalCustomsValue,
                            ExternalCategoryId = item.ExternalCategoryId,
                            ExternalCategoryName = item.ExternalCategoryName
                        };

                        if (item.PurchaseInvoiceUrls != null && item.PurchaseInvoiceUrls.Any())
                        {
                            foreach (var url in item.PurchaseInvoiceUrls)
                            {
                                customsItem.Invoices.Add(new Domain.Entities.CustomsItemInvoice
                                {
                                    InvoicePath = url
                                });
                            }
                        }

                        _context.CustomsItems.Add(customsItem);
                    }
                }

                // ===== OrderService — auto-assign (Pickup only) =====
                var packageServices = await _context.PackageServices
                    .Where(ps => ps.PackageId == order.PackageId)
                    .Include(ps => ps.Service)
                    .ToListAsync(cancellationToken);

                foreach (var packageService in packageServices)
                {
                    DateTime scheduledStart, scheduledEnd;
                    int? assignedEmployeeId = null;
                    var status = Domain.Enums.ServiceStatus.Pending;

                    switch (packageService.ExecutionPhase)
                    {
                        case Domain.Enums.ExecutionPhase.Pickup:
                            var slotParts = draft.SelectedSlot!.Split('-');
                            var slotStart = TimeSpan.Parse(slotParts[0]);
                            var slotEnd = slotParts[1] == "24:00" ? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)) : TimeSpan.Parse(slotParts[1]);
                            scheduledStart = draft.SelectedSlotDate!.Value.Date + slotStart;
                            scheduledEnd = draft.SelectedSlotDate!.Value.Date + slotEnd;
                            break;

                        case Domain.Enums.ExecutionPhase.DepartureCheckin:
                            // Scheduled at end of Pickup slot (will be assigned when Pickup completes)
                            var pickupSlotParts = draft.SelectedSlot!.Split('-');
                            var pickupSlotEnd = pickupSlotParts[1] == "24:00" ? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)) : TimeSpan.Parse(pickupSlotParts[1]);
                            scheduledStart = draft.SelectedSlotDate!.Value.Date + pickupSlotEnd;
                            scheduledEnd = draft.FlightInfo.DepartureTimeUtc.AddHours(-1);
                            break;

                        case Domain.Enums.ExecutionPhase.ArrivalCheckin:
                            // Scheduled at flight arrival time (will be assigned when DepartureCheckin completes)
                            var arrivalTime = draft.FlightInfo.ArrivalTimeUtc ?? draft.FlightInfo.DepartureTimeUtc.AddHours(3);
                            scheduledStart = arrivalTime;
                            scheduledEnd = arrivalTime.AddHours(2);
                            break;

                        case Domain.Enums.ExecutionPhase.Delivery:
                            var deliverySlotParts = draft.SelectedDeliverySlot!.Split('-');
                            var deliverySlotStart = TimeSpan.Parse(deliverySlotParts[0]);
                            var deliverySlotEnd = deliverySlotParts[1] == "24:00" ? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)) : TimeSpan.Parse(deliverySlotParts[1]);
                            scheduledStart = draft.SelectedDeliverySlotDate!.Value.Date + deliverySlotStart;
                            scheduledEnd = draft.SelectedDeliverySlotDate!.Value.Date + deliverySlotEnd;
                            break;

                        default:
                            scheduledStart = draft.SelectedSlotDate!.Value.Date;
                            scheduledEnd = scheduledStart.AddHours(2);
                            break;
                    }

                    _context.OrderServices.Add(new Domain.Entities.OrderService
                    {
                        OrderId = order.OrderId,
                        PackageServiceId = packageService.PackageServiceId,
                        ServiceStatus = status,
                        ScheduledStartTime = scheduledStart,
                        ScheduledEndTime = scheduledEnd,
                        AssignedEmployeeId = assignedEmployeeId,
                        AssignedAt = assignedEmployeeId != null ? DateTime.UtcNow : null
                    });
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await _draftOrderService.RemoveDraftOrderAsync(customerId.ToString(), cancellationToken);

                // Customer Notification — Order Confirmed (Awaiting Payment)
                _context.Notifications.Add(new Domain.Entities.Notification
                {
                    UserId = customerId,
                    UserType = Domain.Enums.UserType.Customer,
                    NotificationType = Domain.Enums.NotificationType.OrderUpdated,
                    Title = "Order Placed — Awaiting Payment",
                    Message = $"Order #{order.OrderId} for Door To Door has been placed successfully. Please complete your payment to activate the service.",
                    NotificationChannel = Domain.Enums.NotificationChannel.InApp,
                    OrderId = order.OrderId
                });
                await _context.SaveChangesAsync(cancellationToken);

                await _pusher.PushToCustomerAsync(
                    customerId,
                    "Order Placed — Awaiting Payment",
                    $"Order #{order.OrderId} for Door To Door has been placed successfully. Please complete your payment to proceed.",
                    "OrderConfirmed",
                    order.OrderId);

                return new ConfirmOrderResponse
                {
                    IsValid = true,
                    Success = true,
                    OrderId = order.OrderId,
                    OrderNumber = $"LTS-{DateTime.UtcNow.Year}-{order.OrderId}",
                    TotalPaid = invoiceDto.Breakdown.TotalAmount,
                    Message = "Order created successfully. Please proceed to payment to finalize your booking."
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                var fullError = ex.InnerException?.InnerException?.Message 
             ?? ex.InnerException?.Message 
             ?? ex.Message;
                return new ConfirmOrderResponse { Success = false, ErrorMessage = fullError };
            }
        });
    }

    public async Task<AvailableDatesResponse> GetAvailableDeliveryDatesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Session not found" };

        if (string.IsNullOrEmpty(draft.PickupFormattedAddress))
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Pickup location must be specified first" };

        if (string.IsNullOrEmpty(draft.DeliveryFormattedAddress))
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Delivery location must be specified first" };

        var arrivalTimeUtc = draft.FlightInfo.ArrivalTimeUtc ?? draft.FlightInfo.DepartureTimeUtc.AddHours(4);
        var earliestDelivery = arrivalTimeUtc.AddHours(4);
        var latestDelivery = earliestDelivery.AddDays(4);

        var availableDates = new List<DateTime>();
        var today = DateTime.UtcNow.Date;
        var startPoint = earliestDelivery.Date < today ? today : earliestDelivery.Date;

        for (var day = startPoint; day <= latestDelivery.Date; day = day.AddDays(1))
        {
            availableDates.Add(day);
        }

        return new AvailableDatesResponse
        {
            IsValid = true,
            AvailableDates = availableDates
        };
    }

    public async Task<AvailableSlotsResponse> GetAvailableDeliverySlotsAsync(int customerId, DateTime date, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Draft order not found. Please start from Step 1." };

        if (string.IsNullOrEmpty(draft.PickupFormattedAddress))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Pickup location must be specified first" };

        if (string.IsNullOrEmpty(draft.DeliveryFormattedAddress))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Delivery location must be specified first" };



        var arrivalTimeUtc = draft.FlightInfo.ArrivalTimeUtc ?? draft.FlightInfo.DepartureTimeUtc.AddHours(4);
        var earliestDelivery = arrivalTimeUtc.AddHours(4);
        var latestDelivery = earliestDelivery.AddDays(4);

        if (date.Date < earliestDelivery.Date)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = $"Cannot select a date before the earliest arrival-based window ({earliestDelivery:yyyy-MM-dd})" };

        if (date.Date > latestDelivery.Date)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = $"Cannot book more than 4 days after delivery start window ({latestDelivery:yyyy-MM-dd})" };

        var response = new AvailableSlotsResponse { IsValid = true };
        TimeSpan? startAfterTimeSpan = null;

        if (date.Date == earliestDelivery.Date)
        {
            startAfterTimeSpan = earliestDelivery.TimeOfDay;
            response.Note = $"Nearest available delivery after {startAfterTimeSpan.Value:hh\\:mm}";
        }

        var allDrivers = await _context.Employees
            .Include(e => e.Vehicle)
            .Where(e => e.JobRole == Domain.Enums.JobRole.Driver 
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

            if (startAfterTimeSpan.HasValue && start < startAfterTimeSpan.Value)
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

            response.AvailableSlots.Add(new SlotItem
            {
                Slot = slot,
                Available = isAvailable
            });
        }

        response.AvailableSlots = response.AvailableSlots
            .Where(s => s.Available)
            .ToList();

        return response;
    }

    private async Task<Domain.Entities.Employee?> FindAvailableDriverAsync(
        DateTime scheduledStart, DateTime scheduledEnd, CancellationToken cancellationToken)
    {
        var slotStart = scheduledStart.TimeOfDay;
        var slotEnd = scheduledEnd.TimeOfDay;
        var date = scheduledStart.Date;

        var drivers = await _context.Employees
            .Include(e => e.Vehicle)
            .Where(e => e.JobRole == Domain.Enums.JobRole.Driver
                     && e.IsActive
                     && !e.IsDeleted
                     && e.VehicleId != null
                     && e.Vehicle!.IsActive
                     && !e.Vehicle.IsDeleted)
            .Include(e => e.AssignedOrderServices)
            .ToListAsync(cancellationToken);

        return drivers.FirstOrDefault(d =>
            IsShiftCovering(d.ShiftType, slotStart, slotEnd) &&
            !HasConflict(d, date, slotStart, slotEnd));
    }

    public async Task AssignEmployeesAfterPaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var servicesToAssign = await _context.OrderServices
            .Where(os => os.OrderId == orderId && os.ServiceStatus == Domain.Enums.ServiceStatus.Pending && os.PackageService.ExecutionPhase == Domain.Enums.ExecutionPhase.Pickup)
            .ToListAsync(cancellationToken);

        foreach (var service in servicesToAssign)
        {
            var driver = await FindAvailableDriverAsync(service.ScheduledStartTime, service.ScheduledEndTime, cancellationToken);
            if (driver != null)
            {
                service.AssignedEmployeeId = driver.EmployeeId;
                service.AssignedAt = DateTime.UtcNow;
                service.ServiceStatus = Domain.Enums.ServiceStatus.Assigned;

                _context.Notifications.Add(new Domain.Entities.Notification
                {
                    UserId = driver.EmployeeId,
                    UserType = Domain.Enums.UserType.Employee,
                    NotificationType = Domain.Enums.NotificationType.OrderUpdated,
                    Title = "You have been assigned to a new order (Payment Confirmed)",
                    Message = $"Luggage Pickup Order - Time: {service.ScheduledStartTime:dd/MM hh:mm tt}",
                    NotificationChannel = Domain.Enums.NotificationChannel.InApp,
                    OrderId = orderId
                });
            }
        }

        // Customer Notification — Driver assigned after payment
        var order = await _context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order != null)
        {
            _context.Notifications.Add(new Domain.Entities.Notification
            {
                UserId = order.CustomerId,
                UserType = Domain.Enums.UserType.Customer,
                NotificationType = Domain.Enums.NotificationType.OrderUpdated,
                Title = "A driver has been assigned",
                Message = "A driver has been assigned to your order and will pick up your luggage at the scheduled time",
                NotificationChannel = Domain.Enums.NotificationChannel.InApp,
                OrderId = orderId
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (order != null)
        {
            await _pusher.PushToCustomerAsync(
                order.CustomerId,
                "A driver has been assigned",
                "A driver has been assigned to your order and will pick up your luggage at the scheduled time",
                "DriverAssigned",
                orderId);
        }
    }

    
    private async Task ValidateTicketNotUsedAsync(string ticketNumber, CancellationToken cancellationToken)
    {
        var existingOrder = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Package)
            .FirstOrDefaultAsync(
                o => o.TicketNumber == ticketNumber
                  && o.OrderStatus != Domain.Enums.OrderStatus.Cancelled,
                cancellationToken);

        if (existingOrder != null)
            throw new InvalidOperationException(
                $"This ticket is already used in the '{existingOrder.Package.PackageName}' service.");
    }
}
