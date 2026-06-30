using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.External.Airline;
using Travora.Application.DTOs.Orders.CarService;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.Interfaces.External;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Customer;
using Travora.Domain.Constants;
using Travora.Domain.Enums;
using Travora.Application.DTOs.Customer.Auth;
using Travora.Infrastructure.Helpers;
using Travora.Domain.Entities;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.CustomerPanel.Services;

public class CarServiceOrderService : ICarServiceOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IAirlineService _airlineService;
    private readonly ICloudinaryService _cloudinaryService;
    private readonly IDraftOrderService _draftOrderService;
    private readonly IGeocodingService _geocodingService;
    private readonly INotificationPusher _pusher;
    private readonly IPassportOcrService _ocrService;
    private readonly IFlightPredictionService _predictionService;

    public CarServiceOrderService(
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

    // ===================================================================
    // STEP 1 — Flight Data Validation + Service Type
    // ===================================================================
    public async Task<CarServiceValidateFlightResponse> ValidateFlightAsync(
        int customerId, CarServiceValidateFlightRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);

        if (customer == null)
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = "Customer not found", ServiceType = request.ServiceType };
        if (string.IsNullOrEmpty(customer.PassportNumber))
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = "Passport number not found, please complete your profile data", ServiceType = request.ServiceType };
        if (customer.AccountStatus != CustomerAccountStatus.Verified)
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = "Your account must be verified to use this service", ServiceType = request.ServiceType };

        if (request.BaggageCount <= 0)
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = "Please enter the number of bags.", ServiceType = request.ServiceType };

        // Call Airline API first to validate ownership & details
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
                : "Flight or ticket data is invalid";
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = errorMsg, ServiceType = request.ServiceType };
        }

        // Cross-service ticket conflict check (after validating ticket ownership)
        // CarToAirport: blocked by ALL packages | CarFromAirport: blocked by all EXCEPT BagTracking
        try
        {
            var targetPackageCode = request.ServiceType == CarServiceType.DeliveryToAirport
                ? PackageCodes.CarServiceToAirport
                : PackageCodes.CarServiceFromAirport;

            await ValidateTicketNotUsedAsync(request.TicketNumber, targetPackageCode, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = ex.Message, ServiceType = request.ServiceType };
        }

        flightData.Terminal = airlineRes.Terminal ?? airlineRes.Ticket?.Flight?.Terminal ?? flightData.Terminal;
        flightData.Gate = airlineRes.Gate ?? airlineRes.Ticket?.Flight?.Gate ?? flightData.Gate;
        flightData.FlightDate = airlineRes.FlightDate ?? flightData.FlightDate;
        flightData.FlightDuration = airlineRes.FlightDuration ?? flightData.FlightDuration;
        flightData.BoardingTimeUtc = airlineRes.BoardingTimeUtc ?? flightData.BoardingTimeUtc;

        passengerData.SeatNumber = airlineRes.Ticket?.SeatNumber ?? passengerData.SeatNumber;
        passengerData.TravelClass = airlineRes.Ticket?.TravelClass ?? passengerData.TravelClass;
        passengerData.BoardingStatus = airlineRes.Ticket?.BoardingStatus ?? passengerData.BoardingStatus;

        if (request.ServiceType != CarServiceType.DeliveryFromAirport)
        {
            var departure = flightData.DepartureTimeUtc;
            var diff = departure - DateTime.UtcNow;
            if (diff.TotalHours < 12)
                return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = "Booking must be made at least 12 hours before departure", ServiceType = request.ServiceType };
        }

        var bookingDeadlineUtc = request.ServiceType == CarServiceType.DeliveryFromAirport
            ? (flightData.ArrivalTimeUtc ?? flightData.DepartureTimeUtc.AddHours(4)).AddDays(4)
            : flightData.DepartureTimeUtc.AddHours(-12);

        var draft = new CarServiceDraftOrder
        {
            CustomerId = customerId.ToString(),
            TicketNumber = request.TicketNumber,
            FlightInfo = flightData,
            PassengerInfo = passengerData,
            BaggageCount = request.BaggageCount,
            BookingDeadlineUtc = bookingDeadlineUtc,
            ServiceType = request.ServiceType
        };

        await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new CarServiceValidateFlightResponse
        {
            IsValid = true,
            FlightInfo = flightData,
            PassengerInfo = passengerData,
            BaggageCount = request.BaggageCount,
            BookingDeadlineUtc = bookingDeadlineUtc,
            ServiceType = request.ServiceType
        };
    }

    // ===================================================================
    // STEP 2 — Add Companions
    // ===================================================================
    public async Task<ValidateCompanionResponse> ValidateCompanionAsync(
        int customerId, ValidateCompanionRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "Session expired or not found, please restart the process" };

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

            ocrResultJson = JsonSerializer.Serialize(ocrResult);
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
                : "Companion data is invalid";
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = errorMsg };
        }

        if (flightData.FlightNumber != draft.FlightInfo.FlightNumber)
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "Companion is not on the same flight" };

        // OCR data takes priority over Airline data
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

        draft.Companions.RemoveAll(c => c.PassportNumber == finalPassportNumber);
        draft.Companions.Add(newCompanion);
        await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

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
                PassportExpiryDate = newCompanion.PassportExpiryDate,
                IsVerified = newCompanion.IsVerified
            },
            TotalCompanions = draft.Companions.Count
        };
    }

    // ===================================================================
    // STEP 2.5 — Total Baggage Validation
    // ===================================================================
    public async Task<ValidateBaggageResponse> ValidateBaggageAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ValidateBaggageResponse { IsValid = false, ErrorMessage = "Session not found" };

        var allTicketNumbers = new List<string> { draft.TicketNumber };
        allTicketNumbers.AddRange(draft.Companions.Select(c => c.TicketNumber));

        if (draft.ServiceType == CarServiceType.DeliveryFromAirport)
        {
            // 1. Baggage Allowance Check
            var allowanceTasks = allTicketNumbers.Select(tn => new
            {
                TicketNumber = tn,
                Task = _airlineService.GetBaggageAllowanceAsync(tn, cancellationToken)
            }).ToList();

            await Task.WhenAll(allowanceTasks.Select(t => t.Task));
            int summedAllowance = allowanceTasks.Sum(t => t.Task.Result.AllowedBaggageCount);

            if (draft.BaggageCount > summedAllowance)
            {
                return new ValidateBaggageResponse
                {
                    IsValid = false,
                    ErrorMessage = "The total number of bags entered exceeds the baggage allowance limit for these tickets"
                };
            }

            // 2. Checked-In Registered bags Check
            var bagTasks = allTicketNumbers.Select(tn => new
            {
                TicketNumber = tn,
                Task = _airlineService.GetBaggageCountAsync(tn, cancellationToken)
            }).ToList();

            await Task.WhenAll(bagTasks.Select(t => t.Task));
            int totalFromAirline = bagTasks.Sum(t => t.Task.Result.TotalBaggageCount);

            if (totalFromAirline < draft.BaggageCount)
            {
                return new ValidateBaggageResponse
                {
                    IsValid = false,
                    ErrorMessage = "Some of the bags you entered have not been registered with the airline yet"
                };
            }

            // Build breakdown
            var breakdown = new List<BaggageBreakdown>();
            foreach (var t in bagTasks)
            {
                var result = t.Task.Result;
                if (result.Tickets != null && result.Tickets.Any())
                {
                    foreach (var ticket in result.Tickets)
                    {
                        breakdown.Add(new BaggageBreakdown
                        {
                            TicketNumber = ticket.TicketNumber ?? t.TicketNumber,
                            BaggageCount = ticket.BaggageCount
                        });
                    }
                }
                else
                {
                    breakdown.Add(new BaggageBreakdown
                    {
                        TicketNumber = t.TicketNumber,
                        BaggageCount = result.TotalBaggageCount
                    });
                }
            }

            draft.TotalBaggageCount = totalFromAirline;
            draft.BaggageValidated = true;

            foreach (var comp in draft.Companions)
            {
                var companionBags = breakdown.FirstOrDefault(b => b.TicketNumber == comp.TicketNumber);
                comp.BaggageCount = companionBags?.BaggageCount ?? 0;
            }

            await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

            return new ValidateBaggageResponse
            {
                IsValid = true,
                TotalBaggageCount = totalFromAirline,
                Breakdown = breakdown
            };
        }
        else
        {
            var allowanceTasks = allTicketNumbers.Select(tn => new
            {
                TicketNumber = tn,
                Task = _airlineService.GetBaggageAllowanceAsync(tn, cancellationToken)
            }).ToList();

            await Task.WhenAll(allowanceTasks.Select(t => t.Task));
            int summedAllowance = allowanceTasks.Sum(t => t.Task.Result.AllowedBaggageCount);

            if (draft.BaggageCount > summedAllowance)
            {
                return new ValidateBaggageResponse
                {
                    IsValid = false,
                    ErrorMessage = "The total number of bags entered exceeds the baggage allowance limit for these tickets"
                };
            }

            draft.TotalBaggageCount = draft.BaggageCount;
            draft.BaggageValidated = true;

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

            await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

            return new ValidateBaggageResponse
            {
                IsValid = true,
                TotalBaggageCount = draft.TotalBaggageCount,
                Breakdown = breakdown
            };
        }
    }

    // ===================================================================
    // STEP 3 — Location (Reverse Geocoding)
    // ===================================================================
    public async Task<ResolveLocationResponse> ResolveLocationAsync(
        int customerId, CarServiceResolveLocationRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Session not found" }; // using return new ErrorMessage since return type changed

        if (!draft.BaggageValidated)
            return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Baggage validation step must be completed first" };

        var result = await _geocodingService.ReverseGeocodeAsync(request.Latitude, request.Longitude, cancellationToken);

        string locationType = draft.ServiceType == CarServiceType.DeliveryToAirport ? "pickup" : "delivery";

        draft.LocationLatitude = request.Latitude;
        draft.LocationLongitude = request.Longitude;
        draft.LocationFormattedAddress = result?.FormattedAddress ?? string.Empty;
        draft.LocationStreetAddress = result?.StreetAddress;
        draft.LocationCity = result?.City;
        draft.LocationState = result?.State;
        draft.LocationCountry = result?.Country;
        draft.LocationPostalCode = result?.PostalCode;

        await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new ResolveLocationResponse
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
            LocationType = locationType
        };
    }

    // ===================================================================
    // STEP 3.5 — Update Location (Manual Correction)
    // ===================================================================
    public async Task<ResolveLocationResponse> UpdateLocationAsync(
        int customerId, CarServiceUpdateLocationRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Session not found" };

        if (string.IsNullOrEmpty(draft.LocationFormattedAddress))
            return new ResolveLocationResponse { IsValid = false, ErrorMessage = "Location must be resolved first before updating" };

        if (request.StreetAddress != null) draft.LocationStreetAddress = request.StreetAddress;
        if (request.City != null) draft.LocationCity = request.City;
        if (request.State != null) draft.LocationState = request.State;
        if (request.Country != null) draft.LocationCountry = request.Country;
        if (request.PostalCode != null) draft.LocationPostalCode = request.PostalCode;

        await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        string locationType = draft.ServiceType == CarServiceType.DeliveryToAirport ? "pickup" : "delivery";

        return new ResolveLocationResponse
        {
            IsValid = true,
            Latitude = draft.LocationLatitude ?? 0,
            Longitude = draft.LocationLongitude ?? 0,
            FormattedAddress = draft.LocationFormattedAddress ?? string.Empty,
            StreetAddress = draft.LocationStreetAddress,
            City = draft.LocationCity,
            State = draft.LocationState,
            Country = draft.LocationCountry,
            PostalCode = draft.LocationPostalCode,
            LocationType = locationType
        };
    }

    // ===================================================================
    // STEP 4 — Slot Selection
    // ===================================================================
    public async Task<AvailableSlotsResponse> GetAvailableSlotsAsync(
        int customerId, DateTime date, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Session not found, please start from the first step" };

        if (string.IsNullOrEmpty(draft.LocationFormattedAddress))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Location selection step must be completed first" };

        var targetAirportCode = draft.ServiceType == CarServiceType.DeliveryToAirport 
            ? draft.FlightInfo.DepartureIataCode 
            : draft.FlightInfo.ArrivalIataCode;

        var airport = await _context.Airports
            .FirstOrDefaultAsync(a => a.CodeIataAirport == targetAirportCode, cancellationToken);

        var response = new AvailableSlotsResponse { IsValid = true };
        DateTime? absoluteCutoffUtc = null;
        DateTime? startAfterUtc = null;

        if (draft.ServiceType == CarServiceType.DeliveryFromAirport)
        {
            var arrivalTimeUtc = draft.FlightInfo.ArrivalTimeUtc ?? draft.FlightInfo.DepartureTimeUtc.AddHours(4);
            var earliestDelivery = arrivalTimeUtc.AddHours(4);
            var latestDelivery = earliestDelivery.AddDays(4);

            var localArrival = TimezoneHelper.ConvertUtcToAirportLocal(airport, arrivalTimeUtc);
            var localEarliest = localArrival.AddHours(4);
            var localLatest = localEarliest.AddDays(4);
            var localDate = TimezoneHelper.ConvertUtcToAirportLocal(airport, date);

            if (localDate.Date < localEarliest.Date || localDate.Date > localLatest.Date)
                return new AvailableSlotsResponse { IsValid = false, ErrorMessage = $"Execution date must be between {localEarliest:yyyy-MM-dd} and {localLatest:yyyy-MM-dd}" };

            startAfterUtc = earliestDelivery;
            if (localDate.Date == localEarliest.Date)
            {
                var localEarliestTime = localEarliest.ToString(@"hh\:mm");
                response.Note = $"Nearest available delivery after {localEarliestTime}";
            }
        }
        else
        {
            var departure = draft.FlightInfo.DepartureTimeUtc;
            var earliestPossible = departure.AddDays(-4);
            var latestPossible = departure.AddHours(-12);

            var localDeparture = TimezoneHelper.ConvertUtcToAirportLocal(airport, departure);
            var localEarliest = localDeparture.AddDays(-4);
            var localLatest = localDeparture.AddHours(-12);
            var localNow = TimezoneHelper.ConvertUtcToAirportLocal(airport, DateTime.UtcNow);
            var localDate = TimezoneHelper.ConvertUtcToAirportLocal(airport, date);

            if (localDate.Date < localNow.Date)
                return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Cannot select a day in the past" };
            
            if (localDate.Date < localEarliest.Date || localDate.Date > localLatest.Date)
                return new AvailableSlotsResponse { IsValid = false, ErrorMessage = $"Execution date must be between {localEarliest:yyyy-MM-dd} and {localLatest:yyyy-MM-dd}" };

            absoluteCutoffUtc = latestPossible;
            if (localDate.Date == localLatest.Date)
            {
                response.CutoffTime = latestPossible.ToString(@"HH\:mm");
                response.Note = $"The last available slot must end before {response.CutoffTime}";
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

        var localTargetDate = TimezoneHelper.ConvertUtcToAirportLocal(airport, date);

        foreach (var slot in slots)
        {
            var parts = slot.Split('-');
            var start = TimeSpan.Parse(parts[0]);
            var end = parts[1] == "24:00" ? TimeSpan.FromHours(24) : TimeSpan.Parse(parts[1]);

            var localStartDt = localTargetDate.Date.Add(start);
            var localEndDt = localTargetDate.Date.Add(end);

            var slotStartUtc = TimezoneHelper.ConvertAirportLocalToUtc(airport, localStartDt);
            var slotEndUtc = TimezoneHelper.ConvertAirportLocalToUtc(airport, localEndDt);

            bool isAvailable = true;

            // Skip slots that have already passed
            if (slotStartUtc < DateTime.UtcNow)
            {
                isAvailable = false;
            }
            else if (absoluteCutoffUtc.HasValue && slotEndUtc > absoluteCutoffUtc.Value)
            {
                isAvailable = false;
            }
            else if (startAfterUtc.HasValue && slotStartUtc < startAfterUtc.Value)
            {
                isAvailable = false;
            }
            else
            {
                var availableDrivers = allDrivers.Where(d =>
                    IsShiftCovering(d.ShiftType, start, end) &&
                    !HasConflict(d, slotStartUtc, slotEndUtc)
                ).ToList();

                if (!availableDrivers.Any())
                    isAvailable = false;
            }

            var formattedUtcSlot = $"{slotStartUtc:HH:mm}-{slotEndUtc:HH:mm}";
            if (slotEndUtc.TimeOfDay == TimeSpan.Zero && slotEndUtc.Date > slotStartUtc.Date)
            {
                formattedUtcSlot = $"{slotStartUtc:HH:mm}-24:00";
            }

            response.AvailableSlots.Add(new SlotItem { Slot = formattedUtcSlot, Available = isAvailable });
        }

        response.AvailableSlots = response.AvailableSlots.Where(s => s.Available).ToList();
        return response;
    }

    public async Task<AvailableDatesResponse> GetAvailableDatesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Session not found" };

        if (string.IsNullOrEmpty(draft.LocationFormattedAddress))
            return new AvailableDatesResponse { IsValid = false, ErrorMessage = "Location selection step must be completed first" };

        var targetAirportCode = draft.ServiceType == CarServiceType.DeliveryToAirport 
            ? draft.FlightInfo.DepartureIataCode 
            : draft.FlightInfo.ArrivalIataCode;

        var airport = await _context.Airports
            .FirstOrDefaultAsync(a => a.CodeIataAirport == targetAirportCode, cancellationToken);

        var availableDates = new List<DateTime>();

        if (draft.ServiceType == CarServiceType.DeliveryFromAirport)
        {
            var arrivalTimeUtc = draft.FlightInfo.ArrivalTimeUtc ?? draft.FlightInfo.DepartureTimeUtc.AddHours(4);
            var earliestDelivery = arrivalTimeUtc.AddHours(4);
            var latestDelivery = earliestDelivery.AddDays(4);

            var localArrival = TimezoneHelper.ConvertUtcToAirportLocal(airport, arrivalTimeUtc);
            var localEarliest = localArrival.AddHours(4);
            var localLatest = localEarliest.AddDays(4);
            var localNow = TimezoneHelper.ConvertUtcToAirportLocal(airport, DateTime.UtcNow);

            var startPoint = localEarliest.Date < localNow.Date ? localNow.Date : localEarliest.Date;

            for (var day = startPoint; day <= localLatest.Date; day = day.AddDays(1))
            {
                var utcMidnight = TimezoneHelper.ConvertAirportLocalToUtc(airport, day.Date);
                availableDates.Add(utcMidnight);
            }
        }
        else
        {
            var departure = draft.FlightInfo.DepartureTimeUtc;
            var earliestPossible = departure.AddDays(-4);
            var latestPossible = departure.AddHours(-12);

            var localDeparture = TimezoneHelper.ConvertUtcToAirportLocal(airport, departure);
            var localEarliest = localDeparture.AddDays(-4);
            var localLatest = localDeparture.AddHours(-12);
            var localNow = TimezoneHelper.ConvertUtcToAirportLocal(airport, DateTime.UtcNow);

            if (localNow >= localLatest)
            {
                return new AvailableDatesResponse 
                { 
                    IsValid = false, 
                    ErrorMessage = "It is too late to book. All bookings must be completed at least 12 hours before the flight departure." 
                };
            }

            var startPoint = localEarliest.Date < localNow.Date ? localNow.Date : localEarliest.Date;

            for (var day = startPoint; day <= localLatest.Date; day = day.AddDays(1))
            {
                var utcMidnight = TimezoneHelper.ConvertAirportLocalToUtc(airport, day.Date);
                availableDates.Add(utcMidnight);
            }
        }

        return new AvailableDatesResponse
        {
            IsValid = true,
            AvailableDates = availableDates
        };
    }

    // ===================================================================
    // STEP 5 — Bags (delivery_from_airport only) — Real data from baggageTags
    // ===================================================================
    public async Task<MyBagsResponse> GetMyBagsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new MyBagsResponse { IsValid = false, ErrorMessage = "Session not found" };

        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new MyBagsResponse { IsValid = false, ErrorMessage = "Appointment selection step must be completed first" };

        if (draft.ServiceType != CarServiceType.DeliveryFromAirport)
            return new MyBagsResponse { IsValid = false, ErrorMessage = "This step is only available for delivery From Airport service" };

        var ticketNumbers = new List<string> { draft.TicketNumber };
        ticketNumbers.AddRange(draft.Companions.Select(c => c.TicketNumber));

        var tasks = ticketNumbers.Select(tn => new
        {
            TicketNumber = tn,
            Task = _airlineService.GetBaggageCountAsync(tn, cancellationToken)
        }).ToList();

        await Task.WhenAll(tasks.Select(t => t.Task));

        var passengerBagItems = new List<PassengerBagItem>();
        var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (draft.PassengerInfo != null)
        {
            nameMap[draft.TicketNumber] = $"{draft.PassengerInfo.FirstName} {draft.PassengerInfo.LastName}".Trim();
        }
        else
        {
            nameMap[draft.TicketNumber] = "Main Passenger";
        }

        foreach (var comp in draft.Companions)
        {
            nameMap[comp.TicketNumber] = $"{comp.FirstName} {comp.LastName}".Trim();
        }

        foreach (var t in tasks)
        {
            var result = t.Task.Result;
            if (result.Tickets != null && result.Tickets.Any())
            {
                foreach (var ticket in result.Tickets)
                {
                    var ticketNum = ticket.TicketNumber ?? t.TicketNumber;
                    if (!nameMap.TryGetValue(ticketNum, out string? passengerName))
                    {
                        passengerName = result.PassengerName ?? "Passenger";
                    }

                    var bagsForTicket = new List<BagItem>();
                    if (ticket.BaggageTags != null)
                    {
                        foreach (var tag in ticket.BaggageTags)
                        {
                            bagsForTicket.Add(new BagItem
                            {
                                TagNumber = tag.TagNumber,
                                WeightKg = tag.WeightKg,
                                Journey = $"{tag.Origin ?? draft.FlightInfo.DepartureAirport} → {tag.Destination ?? draft.FlightInfo.ArrivalAirport}",
                                Gate = tag.Gate ?? draft.FlightInfo.Gate ?? "N/A",
                                Terminal = tag.Terminal ?? draft.FlightInfo.Terminal ?? "N/A",
                                TicketNumber = ticketNum
                            });
                        }
                    }

                    passengerBagItems.Add(new PassengerBagItem
                    {
                        PassengerName = passengerName,
                        TicketNumber = ticketNum,
                        Bags = bagsForTicket
                    });
                }
            }
            else
            {
                nameMap.TryGetValue(t.TicketNumber, out string? passengerName);
                passengerBagItems.Add(new PassengerBagItem
                {
                    PassengerName = passengerName ?? "Passenger",
                    TicketNumber = t.TicketNumber,
                    Bags = new List<BagItem>()
                });
            }
        }

        return new MyBagsResponse { IsValid = true, Passengers = passengerBagItems };
    }

    // ===================================================================
    // STEP 5.5 — Bag Selection
    // ===================================================================
    public async Task SelectBagsAsync(int customerId, SelectBagsRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            throw new Exception("Session not found");

        if (string.IsNullOrEmpty(draft.SelectedSlot))
            throw new Exception("Appointment selection step must be completed first");

        if (draft.ServiceType != CarServiceType.DeliveryFromAirport)
            throw new Exception("This step is only available for delivery From Airport service");

        if (request.SelectedTagNumbers == null || !request.SelectedTagNumbers.Any())
            throw new Exception("At least one bag must be selected");

        if (request.SelectedTagNumbers.Count != draft.BaggageCount)
            throw new Exception($"You must select exactly {draft.BaggageCount} bags, as specified in your initial booking");

        if (request.SelectedTagNumbers.Distinct().Count() != request.SelectedTagNumbers.Count)
            throw new Exception("The same bag cannot be selected twice");

        draft.SelectedBagTags = request.SelectedTagNumbers;
        await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);
    }

    // ===================================================================
    // STEP 6 — Invoice
    // ===================================================================
    public async Task<InvoiceResponse> GetInvoiceAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new InvoiceResponse { IsValid = false, ErrorMessage = "Session not found" };

        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new InvoiceResponse { IsValid = false, ErrorMessage = "Appointment selection step must be completed first" };

        if (draft.ServiceType == CarServiceType.DeliveryFromAirport && !draft.SelectedBagTags.Any())
            return new InvoiceResponse { IsValid = false, ErrorMessage = "Baggage selection step must be completed first" };

        var packageCode = draft.ServiceType == CarServiceType.DeliveryToAirport
            ? PackageCodes.CarServiceToAirport
            : PackageCodes.CarServiceFromAirport;
        var pkg = await _context.Packages.FirstOrDefaultAsync(
            p => p.PackageCode == packageCode, cancellationToken);

        decimal basePrice = pkg?.TotalBasePrice ?? 80m;
        decimal discountAmount = pkg != null ? (pkg.TotalBasePrice * (pkg.Discount ?? 0) / 100) : 0m;

        int incBags = pkg?.IncludedBaggageCount ?? 2;
        decimal extraBagPrice = pkg?.ExtraBaggagePrice ?? 25m;
        int incComps = pkg?.IncludedCompanionsCount ?? 1;
        decimal extraCompPrice = pkg?.ExtraCompanionPrice ?? 20m;

        int totalBags = draft.ServiceType == CarServiceType.DeliveryFromAirport && draft.SelectedBagTags.Any()
            ? draft.SelectedBagTags.Count
            : draft.TotalBaggageCount;

        int extraBags = Math.Max(0, totalBags - incBags);
        decimal extraBagFee = extraBags * extraBagPrice;

        int totalCompanions = draft.Companions.Count;
        int extraComps = Math.Max(0, totalCompanions - incComps);
        decimal extraCompFee = extraComps * extraCompPrice;

        decimal subtotal = basePrice + extraBagFee + extraCompFee;
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
                    TotalBags = totalBags,
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
                CustomsValue = 0,
                CustomsFee = 0,
                Subtotal = subtotal,
                TaxAmount = Math.Round(taxAmount, 2),
                Discount = Math.Round(discountAmount, 2),
                TotalAmount = Math.Round(totalAmount, 2)
            }
        };
    }

    // ===================================================================
    // STEP 7 — Confirm Order
    // ===================================================================
    public async Task<ConfirmOrderResponse> ConfirmOrderAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Session not found" };

        if (!draft.BaggageValidated)
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Baggage validation step must be completed first" };
        if (string.IsNullOrEmpty(draft.LocationFormattedAddress))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Location selection step must be completed first" };
        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Appointment selection step must be completed first" };
        if (draft.ServiceType == CarServiceType.DeliveryFromAirport && !draft.SelectedBagTags.Any())
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Baggage selection step must be completed first" };

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var invoiceDto = await GetInvoiceAsync(customerId, cancellationToken);

                var packageCode = draft.ServiceType == CarServiceType.DeliveryToAirport
                    ? PackageCodes.CarServiceToAirport
                    : PackageCodes.CarServiceFromAirport;
                var pkg = await _context.Packages.FirstOrDefaultAsync(
                    p => p.PackageCode == packageCode, cancellationToken);

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
                        FlightStatus = FlightStatus.Scheduled,
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

                // Locations
                Domain.Entities.Location pickupLocation, deliveryLocation;
                if (draft.ServiceType == CarServiceType.DeliveryToAirport)
                {
                    pickupLocation = new Domain.Entities.Location
                    {
                        StreetAddress = draft.LocationStreetAddress ?? draft.LocationFormattedAddress ?? string.Empty,
                        City = draft.LocationCity ?? string.Empty,
                        State = draft.LocationState ?? string.Empty,
                        Country = draft.LocationCountry ?? string.Empty,
                        PostalCode = draft.LocationPostalCode ?? string.Empty,
                        GpsLatitude = (decimal)(draft.LocationLatitude ?? 0),
                        GpsLongitude = (decimal)(draft.LocationLongitude ?? 0),
                        LocationType = LocationType.Pickup,
                        CustomerId = customerId
                    };
                    deliveryLocation = new Domain.Entities.Location
                    {
                        StreetAddress = $"{draft.FlightInfo.DepartureAirport} Airport",
                        City = draft.FlightInfo.OriginCity ?? string.Empty,
                        State = string.Empty,
                        Country = string.Empty,
                        PostalCode = string.Empty,
                        GpsLatitude = 0,
                        GpsLongitude = 0,
                        LocationType = LocationType.Delivery,
                        CustomerId = customerId
                    };
                }
                else
                {
                    pickupLocation = new Domain.Entities.Location
                    {
                        StreetAddress = $"{draft.FlightInfo.ArrivalAirport} Airport",
                        City = draft.FlightInfo.DestinationCity ?? string.Empty,
                        State = string.Empty,
                        Country = string.Empty,
                        PostalCode = string.Empty,
                        GpsLatitude = 0,
                        GpsLongitude = 0,
                        LocationType = LocationType.Pickup,
                        CustomerId = customerId
                    };
                    deliveryLocation = new Domain.Entities.Location
                    {
                        StreetAddress = draft.LocationStreetAddress ?? draft.LocationFormattedAddress ?? string.Empty,
                        City = draft.LocationCity ?? string.Empty,
                        State = draft.LocationState ?? string.Empty,
                        Country = draft.LocationCountry ?? string.Empty,
                        PostalCode = draft.LocationPostalCode ?? string.Empty,
                        GpsLatitude = (decimal)(draft.LocationLatitude ?? 0),
                        GpsLongitude = (decimal)(draft.LocationLongitude ?? 0),
                        LocationType = LocationType.Delivery,
                        CustomerId = customerId
                    };
                }

                _context.Locations.Add(pickupLocation);
                _context.Locations.Add(deliveryLocation);
                await _context.SaveChangesAsync(cancellationToken);

                // Order
                var targetAirportCode = draft.ServiceType == CarServiceType.DeliveryToAirport 
                    ? draft.FlightInfo.DepartureIataCode 
                    : draft.FlightInfo.ArrivalIataCode;

                var airport = await _context.Airports
                    .FirstOrDefaultAsync(a => a.CodeIataAirport == targetAirportCode, cancellationToken);

                var order = new Domain.Entities.Order
                {
                    CustomerId = customerId,
                    FlightId = flight.FlightId,
                    PackageId = pkg?.PackageId ?? 1,
                    PickupLocationId = pickupLocation.LocationId,
                    DeliveryLocationId = deliveryLocation.LocationId,
                    OrderStatus = OrderStatus.Pending,
                    TicketNumber = draft.TicketNumber,
                    ExtraCompanionsCount = invoiceDto.Breakdown.CompanionDetails.ExtraCompanions,
                    ExtraCompanionsFee = invoiceDto.Breakdown.CompanionDetails.ExtraCompanionsFee,
                    TotalBaggageCount = invoiceDto.Breakdown.BaggageDetails.TotalBags,
                    ExtraBaggageCount = invoiceDto.Breakdown.BaggageDetails.ExtraBags,
                    ExtraBaggageFee = invoiceDto.Breakdown.BaggageDetails.ExtraBaggageFee,
                    TotalAmount = invoiceDto.Breakdown.TotalAmount,
                    PickupDate = draft.ServiceType == CarServiceType.DeliveryToAirport 
                        ? draft.SelectedSlotDate!.Value 
                        : (draft.FlightInfo.ArrivalTimeUtc?.Date ?? draft.FlightInfo.DepartureTimeUtc.Date),
                    PickupTimeSlot = draft.ServiceType == CarServiceType.DeliveryToAirport 
                        ? (draft.SelectedSlot ?? "10:00-12:00") 
                        : "N/A",
                    DeliveryDate = draft.ServiceType == CarServiceType.DeliveryToAirport 
                        ? draft.FlightInfo.DepartureTimeUtc.Date 
                        : draft.SelectedSlotDate!.Value,
                    DeliveryTimeSlot = draft.ServiceType == CarServiceType.DeliveryToAirport 
                        ? "N/A" 
                        : (draft.SelectedSlot ?? "10:00-12:00")
                };

                _context.Orders.Add(order);
                await _context.SaveChangesAsync(cancellationToken);

                // Invoice
                var invoice = new Domain.Entities.Invoice
                {
                    InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-{new Random().Next(1000, 9999)}",
                    OrderId = order.OrderId,
                    PackageFee = invoiceDto.Breakdown.PackageValue,
                    CustomsFee = 0,
                    Subtotal = invoiceDto.Breakdown.Subtotal,
                    TaxAmount = invoiceDto.Breakdown.TaxAmount,
                    TotalAmount = invoiceDto.Breakdown.TotalAmount,
                    InvoiceStatus = InvoiceStatus.Pending,
                    DueDate = DateTime.UtcNow
                };
                _context.Invoices.Add(invoice);

                // Companions
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
                            PassportExpiryDate = comp.PassportExpiryDate,
                            IsVerified = comp.IsVerified
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
                        companionEntity.IsVerified = comp.IsVerified || companionEntity.IsVerified;
                    }
                    await _context.SaveChangesAsync(cancellationToken);
                    companionIdMap[comp.PassportNumber] = companionEntity.CompanionId;

                    var documentExists = await _context.Documents.AnyAsync(d =>
                        d.OwnerId == companionEntity.CompanionId &&
                        d.OwnerType == DocumentOwnerType.Companion &&
                        d.DocumentType == DocumentType.Passport, cancellationToken);

                    if (!documentExists && !string.IsNullOrEmpty(comp.PassportImageUrl))
                    {
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
                    }

                    _context.OrderCompanions.Add(new Domain.Entities.OrderCompanion
                    {
                        OrderId = order.OrderId,
                        CompanionId = companionEntity.CompanionId,
                        TicketNumber = comp.TicketNumber
                    });
                }

                // Baggages
                if (draft.ServiceType == CarServiceType.DeliveryFromAirport && draft.SelectedBagTags.Any())
                {
                    // Build tagNumber → ticketNumber map from airline API
                    var bagTagOwnerMap = new Dictionary<string, string>();
                    var allTickets = new List<string> { draft.TicketNumber };
                    allTickets.AddRange(draft.Companions.Select(c => c.TicketNumber));

                    var bagTasks = allTickets.Select(tn => new
                    {
                        TicketNumber = tn,
                        Task = _airlineService.GetBaggageCountAsync(tn, cancellationToken)
                    }).ToList();
                    await Task.WhenAll(bagTasks.Select(t => t.Task));

                    foreach (var t in bagTasks)
                    {
                        var result = t.Task.Result;
                        if (result.Tickets != null)
                            foreach (var ticket in result.Tickets)
                                if (ticket.BaggageTags != null)
                                    foreach (var tag in ticket.BaggageTags)
                                        bagTagOwnerMap[tag.TagNumber] = t.TicketNumber;
                    }

                    foreach (var tag in draft.SelectedBagTags)
                    {
                        var ownerTicket = bagTagOwnerMap.GetValueOrDefault(tag, draft.TicketNumber);

                        if (ownerTicket == draft.TicketNumber)
                        {
                            _context.Baggages.Add(new Domain.Entities.Baggage
                            {
                                OrderId = order.OrderId,
                                CustomerId = customerId,
                                OwnerType = BaggageOwnerType.Customer,
                                BaggageNumber = tag
                            });
                        }
                        else
                        {
                            var comp = draft.Companions.FirstOrDefault(c => c.TicketNumber == ownerTicket);
                            if (comp != null && companionIdMap.TryGetValue(comp.PassportNumber, out int compId))
                            {
                                _context.Baggages.Add(new Domain.Entities.Baggage
                                {
                                    OrderId = order.OrderId,
                                    CustomerId = customerId,
                                    CompanionId = compId,
                                    OwnerType = BaggageOwnerType.Companion,
                                    BaggageNumber = tag
                                });
                            }
                        }
                    }
                }
                else
                {
                    var primaryBaggageCount = draft.TotalBaggageCount - draft.Companions.Sum(c => c.BaggageCount);
                    for (int i = 0; i < primaryBaggageCount; i++)
                    {
                        _context.Baggages.Add(new Domain.Entities.Baggage
                        {
                            OrderId = order.OrderId,
                            CustomerId = customerId,
                            OwnerType = BaggageOwnerType.Customer
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
                                    OwnerType = BaggageOwnerType.Companion
                                });
                            }
                        }
                    }
                }

                // OrderServices — phase-aware scheduling
                var packageServices = await _context.PackageServices
                    .Where(ps => ps.PackageId == order.PackageId)
                    .Include(ps => ps.Service)
                    .OrderBy(ps => ps.ExecutionPhase)
                    .ToListAsync(cancellationToken);

                foreach (var packageService in packageServices)
                {
                    DateTime scheduledStart, scheduledEnd;

                    switch (packageService.ExecutionPhase)
                    {
                        case ExecutionPhase.Pickup:
                            var pickupTimes = TimezoneHelper.GetSlotUtcTimes(airport, draft.SelectedSlotDate!.Value, draft.SelectedSlot!);
                            scheduledStart = pickupTimes.StartUtc;
                            scheduledEnd = pickupTimes.EndUtc;
                            break;

                        case ExecutionPhase.DepartureCheckin:
                            // To Airport: scheduled at end of Pickup slot (assigned when Pickup completes)
                            var checkinTimes = TimezoneHelper.GetSlotUtcTimes(airport, draft.SelectedSlotDate!.Value, draft.SelectedSlot!);
                            scheduledStart = checkinTimes.EndUtc;
                            scheduledEnd = draft.FlightInfo.DepartureTimeUtc.AddHours(-1);
                            break;

                        case ExecutionPhase.ArrivalCheckin:
                        {
                            // From Airport: BaggageHandler retrieves bags after plane arrives
                            var arrivalTime = draft.FlightInfo.ArrivalTimeUtc ?? draft.FlightInfo.DepartureTimeUtc.AddHours(3);
                            scheduledStart = arrivalTime;
                            scheduledEnd = arrivalTime.AddHours(2);
                            break;
                        }

                        case ExecutionPhase.Delivery:
                            var deliveryTimes = TimezoneHelper.GetSlotUtcTimes(airport, draft.SelectedSlotDate!.Value, draft.SelectedSlot!);
                            scheduledStart = deliveryTimes.StartUtc;
                            scheduledEnd = deliveryTimes.EndUtc;
                            break;

                        default:
                            var defaultTimes = TimezoneHelper.GetSlotUtcTimes(airport, draft.SelectedSlotDate!.Value, draft.SelectedSlot!);
                            scheduledStart = defaultTimes.StartUtc;
                            scheduledEnd = defaultTimes.EndUtc;
                            break;
                    }

                    _context.OrderServices.Add(new Domain.Entities.OrderService
                    {
                        OrderId = order.OrderId,
                        PackageServiceId = packageService.PackageServiceId,
                        ServiceStatus = ServiceStatus.Pending,
                        ScheduledStartTime = scheduledStart,
                        ScheduledEndTime = scheduledEnd,
                        AssignedEmployeeId = null,
                        AssignedAt = null
                    });
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await _draftOrderService.RemoveCarServiceDraftAsync(customerId.ToString(), cancellationToken);

                // Customer Notification — Order Confirmed (Awaiting Payment)
                var serviceLabel = draft.ServiceType == CarServiceType.DeliveryToAirport
                    ? "Car Service (To Airport)"
                    : "Car Service (From Airport)";
                _context.Notifications.Add(new Domain.Entities.Notification
                {
                    UserId = customerId,
                    UserType = UserType.Customer,
                    NotificationType = NotificationType.OrderUpdated,
                    Title = "Order Placed — Awaiting Payment",
                    Message = $"Order #{order.OrderId} for {serviceLabel} has been placed successfully. Please complete your payment to activate the service.",
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = order.OrderId
                });
                await _context.SaveChangesAsync(cancellationToken);

                await _pusher.PushToCustomerAsync(
                    customerId,
                    "Order Placed — Awaiting Payment",
                    $"Order #{order.OrderId} for {serviceLabel} has been placed successfully. Please complete your payment to proceed.",
                    "OrderConfirmed",
                    order.OrderId);

                return new ConfirmOrderResponse
                {
                    IsValid = true,
                    Success = true,
                    OrderId = order.OrderId,
                    OrderNumber = $"LTS-{DateTime.UtcNow.Year}-{order.OrderId}",
                    TotalPaid = invoiceDto.Breakdown.TotalAmount,
                    Message = $"Order created successfully. Please proceed to payment to finalize your booking."
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

    // ===================================================================
    // STEP 8 — Assign driver after payment + notify customer
    // ===================================================================
    public async Task AssignEmployeesAfterPaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight).ThenInclude(f => f.DepartureAirport)
            .Include(o => o.Flight).ThenInclude(f => f.ArrivalAirport)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null) return;

        var airport = order.Package.PackageCode == PackageCodes.CarServiceToAirport
            ? order.Flight.DepartureAirport
            : order.Flight.ArrivalAirport;

        // Get only the FIRST phase to assign (ordered by ExecutionPhase)
        var firstPendingService = await _context.OrderServices
            .Include(os => os.PackageService)
            .Where(os => os.OrderId == orderId && os.ServiceStatus == ServiceStatus.Pending)
            .OrderBy(os => os.PackageService.ExecutionPhase)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstPendingService == null) return;

        var phase = firstPendingService.PackageService.ExecutionPhase;

        if (phase == ExecutionPhase.Pickup)
        {
            // Pickup phase → assign a Driver
            var driver = await FindAvailableDriverAsync(
                airport,
                firstPendingService.ScheduledStartTime,
                firstPendingService.ScheduledEndTime,
                cancellationToken);

            if (driver != null)
            {
                firstPendingService.AssignedEmployeeId = driver.EmployeeId;
                firstPendingService.AssignedAt = DateTime.UtcNow;
                firstPendingService.ServiceStatus = ServiceStatus.Assigned;

                _context.Notifications.Add(new Domain.Entities.Notification
                {
                    UserId = driver.EmployeeId,
                    UserType = UserType.Employee,
                    NotificationType = NotificationType.OrderUpdated,
                    Title = "You have been assigned to a new pickup (Car Service)",
                    Message = $"Pickup request - Time: {firstPendingService.ScheduledStartTime:dd/MM hh:mm tt}",
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = orderId
                });
            }
        }
        else if (phase is ExecutionPhase.DepartureCheckin or ExecutionPhase.ArrivalCheckin)
        {
            // AirportCheckin phase → assign a BaggageHandler
            var handler = await _context.Employees
                .Where(e => e.JobRole == JobRole.BaggageHandler
                         && e.IsActive
                         && !e.IsDeleted)
                .Include(e => e.AssignedOrderServices)
                .FirstOrDefaultAsync(h =>
                    !h.AssignedOrderServices.Any(s =>
                        s.ServiceStatus == ServiceStatus.InProgress ||
                        s.ServiceStatus == ServiceStatus.Assigned), cancellationToken);

            if (handler != null)
            {
                firstPendingService.AssignedEmployeeId = handler.EmployeeId;
                firstPendingService.AssignedAt = DateTime.UtcNow;
                firstPendingService.ServiceStatus = ServiceStatus.Assigned;

                _context.Notifications.Add(new Domain.Entities.Notification
                {
                    UserId = handler.EmployeeId,
                    UserType = UserType.Employee,
                    NotificationType = NotificationType.OrderUpdated,
                    Title = "You have been assigned to receive bags at the airport",
                    Message = $"Airport pickup - Time: {firstPendingService.ScheduledStartTime:dd/MM hh:mm tt}",
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = orderId
                });
            }
        }

        // Notify customer
        _context.Notifications.Add(new Domain.Entities.Notification
        {
            UserId = order.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = "Order confirmed",
            Message = "An employee has been successfully assigned to your request",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = orderId
        });

        await _pusher.PushToCustomerAsync(
            order.CustomerId,
            "Order confirmed",
            "An employee has been successfully assigned to your request",
            "OrderConfirmed",
            orderId);

        await _context.SaveChangesAsync(cancellationToken);
    }

    // ===================================================================
    // HELPERS
    // ===================================================================

    private bool IsShiftCovering(ShiftType shift, TimeSpan slotStart, TimeSpan slotEnd)
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

    private bool HasConflict(Domain.Entities.Employee driver, DateTime slotStartUtc, DateTime slotEndUtc)
    {
        return driver.AssignedOrderServices.Any(os =>
            os.ScheduledStartTime < slotEndUtc &&
            os.ScheduledEndTime > slotStartUtc
        );
    }

    private async Task<Domain.Entities.Employee?> FindAvailableDriverAsync(
        Airport? airport, DateTime scheduledStart, DateTime scheduledEnd, CancellationToken cancellationToken)
    {
        var localStart = TimezoneHelper.ConvertUtcToAirportLocal(airport, scheduledStart);
        var localEnd = TimezoneHelper.ConvertUtcToAirportLocal(airport, scheduledEnd);

        var localEndTd = localEnd.TimeOfDay;
        if (localEndTd == TimeSpan.Zero && localEnd.Date > localStart.Date)
        {
            localEndTd = TimeSpan.FromHours(24);
        }

        var drivers = await _context.Employees
            .Include(e => e.Vehicle)
            .Where(e => e.JobRole == JobRole.Driver 
                     && e.IsActive 
                     && !e.IsDeleted
                     && e.VehicleId != null
                     && e.Vehicle!.IsActive
                     && !e.Vehicle.IsDeleted)
            .Include(e => e.AssignedOrderServices)
            .ToListAsync(cancellationToken);

        return drivers.FirstOrDefault(d =>
            IsShiftCovering(d.ShiftType, localStart.TimeOfDay, localEndTd) &&
            !HasConflict(d, scheduledStart, scheduledEnd));
    }

    
    private async Task ValidateTicketNotUsedAsync(
        string ticketNumber,
        string targetPackageCode,
        CancellationToken cancellationToken)
    {
        // Determine which existing packages block this booking
        string[] blockingCodes = targetPackageCode == PackageCodes.CarServiceToAirport
            ? new[]
            {
                PackageCodes.DoorToDoor,
                PackageCodes.CarServiceToAirport,
                PackageCodes.CarServiceFromAirport,
                PackageCodes.TrackingBaggage
            }
            : new[]   // CarServiceFromAirport (Delivery) — only blocked by DoorToDoor and CarServiceFromAirport
            {
                PackageCodes.DoorToDoor,
                PackageCodes.CarServiceFromAirport
            };

        var existingOrder = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Package)
            .FirstOrDefaultAsync(
                o => o.TicketNumber == ticketNumber
                  && blockingCodes.Contains(o.Package.PackageCode)
                  && o.OrderStatus != OrderStatus.Cancelled,
                cancellationToken);

        if (existingOrder != null)
            throw new InvalidOperationException(
                $"This ticket is already used in the '{existingOrder.Package.PackageName}' service " +
                "and cannot be combined with this Car Service booking.");
    }
}
