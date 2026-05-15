using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.External.Airline;
using Travora.Application.DTOs.Orders.BagTracking;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.Interfaces.External;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Customer;
using Travora.Domain.Constants;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.CustomerPanel.Services;

public class BagTrackingOrderService : IBagTrackingOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IAirlineService _airlineService;
    private readonly ICloudinaryService _cloudinaryService;
    private readonly IDraftOrderService _draftOrderService;
    private readonly INotificationPusher _pusher;

    public BagTrackingOrderService(
        ApplicationDbContext context,
        IAirlineService airlineService,
        ICloudinaryService cloudinaryService,
        IDraftOrderService draftOrderService,
        INotificationPusher pusher)
    {
        _context = context;
        _airlineService = airlineService;
        _cloudinaryService = cloudinaryService;
        _draftOrderService = draftOrderService;
        _pusher = pusher;
    }

    // ===================================================================
    // STEP 1 — validate-flight
    // ===================================================================
    public async Task<ValidateFlightResponse> ValidateFlightAsync(
        int customerId, BagTrackingValidateFlightRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);

        if (customer == null)
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "Customer not found" };
        if (string.IsNullOrEmpty(customer.PassportNumber))
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "Passport number not found, please complete your profile data" };
        if (customer.AccountStatus != CustomerAccountStatus.Verified)
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "Your account must be verified to use this service" };

        // Check if ticket is used in the same package
        try
        {
            await ValidateTicketNotUsedAsync(request.TicketNumber, PackageNames.TrackingBaggage, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = ex.Message };
        }

        // Call Airline API
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
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = errorMsg };
        }

        flightData.Terminal = airlineRes.Terminal ?? airlineRes.Ticket?.Flight?.Terminal ?? flightData.Terminal;
        flightData.Gate = airlineRes.Gate ?? airlineRes.Ticket?.Flight?.Gate ?? flightData.Gate;
        flightData.FlightDate = airlineRes.FlightDate ?? flightData.FlightDate;
        flightData.FlightDuration = airlineRes.FlightDuration ?? flightData.FlightDuration;
        flightData.BoardingTimeUtc = airlineRes.BoardingTimeUtc ?? flightData.BoardingTimeUtc;

        passengerData.SeatNumber = airlineRes.Ticket?.SeatNumber ?? passengerData.SeatNumber;
        passengerData.TravelClass = airlineRes.Ticket?.TravelClass ?? passengerData.TravelClass;
        passengerData.BoardingStatus = airlineRes.Ticket?.BoardingStatus ?? passengerData.BoardingStatus;

        var departure = flightData.DepartureTimeUtc;
        var diff = departure - DateTime.UtcNow;
        if (diff.TotalHours < 12)
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "Booking must be made at least 12 hours before departure" };

        var bookingDeadlineUtc = departure.AddHours(-12);

        var draft = new BagTrackingDraftOrder
        {
            CustomerId = customerId.ToString(),
            TicketNumber = request.TicketNumber,
            FlightInfo = flightData,
            PassengerInfo = passengerData,
            BaggageCount = request.BaggageCount,
            BookingDeadlineUtc = bookingDeadlineUtc
        };

        await _draftOrderService.SaveBagTrackingDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new ValidateFlightResponse
        {
            IsValid = true,
            FlightInfo = flightData,
            PassengerInfo = passengerData,
            BaggageCount = request.BaggageCount,
            BookingDeadlineUtc = bookingDeadlineUtc
        };
    }

    // ===================================================================
    // STEP 2 — validate-companion (Optional)
    // ===================================================================
    public async Task<ValidateCompanionResponse> ValidateCompanionAsync(
        int customerId, ValidateCompanionRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetBagTrackingDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "Session expired or not found, please restart the process" };

        if (request.PassportNumber == draft.PassengerInfo?.PassportNumber)
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "You cannot add yourself as a companion" };

        var airlineReq = new AirlineValidateTicketRequest
        {
            PassportNumber = request.PassportNumber,
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

        string imageUrl = "https://res.cloudinary.com/travora/image/upload/vdefault/companion.jpg";
        if (request.PassportImage != null && request.PassportImage.Length > 0)
        {
            using var stream = request.PassportImage.OpenReadStream();
            var uploadResult = await _cloudinaryService.UploadFileAsync(stream, request.PassportImage.FileName, "travora/companions");
            if (!string.IsNullOrEmpty(uploadResult))
                imageUrl = uploadResult;
        }

        var newCompanion = new DraftCompanion
        {
            FirstName = passengerData.FirstName ?? string.Empty,
            LastName = passengerData.LastName ?? string.Empty,
            PassportNumber = request.PassportNumber,
            TicketNumber = request.TicketNumber,
            SeatNumber = airlineRes.Ticket?.SeatNumber ?? passengerData.SeatNumber ?? string.Empty,
            PassportImageUrl = imageUrl,
            Nationality = passengerData.Nationality,
            DateOfBirth = DateTime.TryParse(passengerData.DateOfBirth, out var dob) ? dob : null,
            PassportExpiryDate = DateTime.TryParse(passengerData.PassportExpiryDate, out var expiry) ? expiry : null
        };

        if (!draft.Companions.Any(c => c.PassportNumber == request.PassportNumber))
        {
            draft.Companions.Add(newCompanion);
            await _draftOrderService.SaveBagTrackingDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);
        }

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

    // ===================================================================
    // STEP 2.5 — validate-baggage
    // ===================================================================
    public async Task<ValidateBaggageResponse> ValidateBaggageAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetBagTrackingDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ValidateBaggageResponse { IsValid = false, ErrorMessage = "Session not found" };

        var tasks = new List<(string TicketNumber, Task<AirlineBaggageCheckResponse> Task)>
        {
            (draft.TicketNumber, _airlineService.GetBaggageCountAsync(draft.TicketNumber, cancellationToken))
        };

        foreach (var comp in draft.Companions)
            tasks.Add((comp.TicketNumber, _airlineService.GetBaggageCountAsync(comp.TicketNumber, cancellationToken)));

        await Task.WhenAll(tasks.Select(t => t.Task));

        var breakdown = tasks.Select(t => new BaggageBreakdown
        {
            TicketNumber = t.TicketNumber,
            BaggageCount = t.Task.Result.TotalBaggageCount
        }).ToList();

        int totalFromAirline = breakdown.Sum(b => b.BaggageCount);

        if (draft.BaggageCount != totalFromAirline)
        {
            return new ValidateBaggageResponse
            {
                IsValid = false,
                ErrorCode = "BaggageCountMismatch",
                ErrorMessage = "The number of bags entered does not match the airline records",
                Expected = totalFromAirline,
                Actual = draft.BaggageCount,
                TotalBaggageCount = totalFromAirline,
                Breakdown = breakdown
            };
        }

        draft.TotalBaggageCount = totalFromAirline;
        draft.BaggageValidated = true;

        foreach (var comp in draft.Companions)
        {
            var companionBags = breakdown.FirstOrDefault(b => b.TicketNumber == comp.TicketNumber);
            comp.BaggageCount = companionBags?.BaggageCount ?? 0;
        }

        await _draftOrderService.SaveBagTrackingDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new ValidateBaggageResponse
        {
            IsValid = true,
            TotalBaggageCount = totalFromAirline,
            Breakdown = breakdown
        };
    }

    // ===================================================================
    // STEP 3 — scan-bag
    // ===================================================================
    public async Task<ScanBagResponse> ScanBagAsync(int customerId, ScanBagRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetBagTrackingDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ScanBagResponse { Found = false, ErrorMessage = "Session not found" };

        if (!draft.BaggageValidated)
            return new ScanBagResponse { Found = false, ErrorMessage = "Baggage validation step must be completed first" };

        // Collect all ticketNumbers
        var ticketNumbers = new List<string> { draft.TicketNumber };
        ticketNumbers.AddRange(draft.Companions.Select(c => c.TicketNumber));

        // Call baggage-check in parallel
        var bagTasks = ticketNumbers.Select(tn => new
        {
            TicketNumber = tn,
            Task = _airlineService.GetBaggageCountAsync(tn, cancellationToken)
        }).ToList();

        await Task.WhenAll(bagTasks.Select(t => t.Task));

        // Extract all baggageTags
        var allTags = new List<AirlineBaggageTag>();
        foreach (var t in bagTasks)
        {
            var result = t.Task.Result;
            if (result.Tickets != null)
                foreach (var ticket in result.Tickets)
                    if (ticket.BaggageTags != null)
                        allTags.AddRange(ticket.BaggageTags);
        }

        // Check if qrData is in the list
        var matchedTag = allTags.FirstOrDefault(tag => tag.TagNumber == request.QrData);
        if (matchedTag == null)
            return new ScanBagResponse { Found = false, ErrorMessage = "This bag does not belong to you" };

        // Check if the bag has already been scanned
        if (draft.ScannedBags.Any(sb => sb.TagNumber == request.QrData))
            return new ScanBagResponse { Found = false, ErrorMessage = "This bag has already been scanned" };

        // Save to draft
        var scannedBag = new DraftScannedBag
        {
            TagNumber = request.QrData,
            WeightKg = matchedTag.WeightKg,
            Destination = matchedTag.Destination,
            ScannedAt = DateTime.UtcNow
        };
        draft.ScannedBags.Add(scannedBag);

        await _draftOrderService.SaveBagTrackingDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new ScanBagResponse
        {
            Found = true,
            Bag = new ScannedBagDto
            {
                TagNumber = scannedBag.TagNumber,
                WeightKg = scannedBag.WeightKg,
                Destination = scannedBag.Destination,
                ScannedAt = scannedBag.ScannedAt
            },
            TotalScanned = draft.ScannedBags.Count,
            TotalRequired = draft.TotalBaggageCount
        };
    }

    // ===================================================================
    // STEP 4 — upload-bag-photos
    // ===================================================================
    public async Task<UploadBagPhotosResponse> UploadBagPhotosAsync(
        int customerId, string tagNumber, List<IFormFile> photos, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetBagTrackingDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            return new UploadBagPhotosResponse { Saved = false, ErrorMessage = "Session not found" };

        var bag = draft.ScannedBags.FirstOrDefault(sb => sb.TagNumber == tagNumber);
        if (bag == null)
            return new UploadBagPhotosResponse { Saved = false, ErrorMessage = "Bag has not been scanned" };

        if (photos == null || !photos.Any())
            return new UploadBagPhotosResponse { Saved = false, ErrorMessage = "At least one photo must be uploaded" };

        // Minimum check — at least 3 photos required
        if (photos.Count < 3)
            return new UploadBagPhotosResponse { Saved = false, ErrorMessage = "At least 3 photos must be uploaded for each bag" };

        // Maximum check — no more than 6 photos per bag (draft + DB)
        int existingInDraft = bag.Photos.Count;
        int existingInDb = await _context.BaggagePhotos
            .CountAsync(bp => bp.Baggage.BaggageNumber == tagNumber
                           && bp.Baggage.CustomerId == customerId, cancellationToken);
        int totalExisting = existingInDraft + existingInDb;

        if (totalExisting >= 6)
            return new UploadBagPhotosResponse { Saved = false, ErrorMessage = $"Maximum 6 photos per bag, you already have {totalExisting} photos" };

        if (totalExisting + photos.Count > 6)
            return new UploadBagPhotosResponse { Saved = false, ErrorMessage = $"Maximum 6 photos per bag, you already have {totalExisting} photos and can only upload {6 - totalExisting} more" };

        var uploadedUrls = new List<string>();
        foreach (var photo in photos)
        {
            using var stream = photo.OpenReadStream();
            var url = await _cloudinaryService.UploadFileAsync(stream, photo.FileName, "travora/baggage");
            uploadedUrls.Add(url);
        }

        bag.Photos.AddRange(uploadedUrls);
        await _draftOrderService.SaveBagTrackingDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new UploadBagPhotosResponse
        {
            TagNumber = tagNumber,
            Photos = bag.Photos,
            Saved = true
        };
    }

    // ===================================================================
    // STEP 5 — invoice
    // ===================================================================
    public async Task<InvoiceResponse> GetInvoiceAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetBagTrackingDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            return new InvoiceResponse { IsValid = false, ErrorMessage = "Session not found" };

        // Check if all scanned bags have photos
        var bagsWithoutPhotos = draft.ScannedBags.Where(sb => !sb.Photos.Any()).ToList();
        if (bagsWithoutPhotos.Any())
            return new InvoiceResponse { IsValid = false, ErrorMessage = "At least one photo must be added for each bag before review" };

        var pkg = await _context.Packages.FirstOrDefaultAsync(
            p => p.PackageName == PackageNames.TrackingBaggage, cancellationToken);

        decimal basePrice = pkg?.TotalBasePrice ?? 80m;
        decimal discountAmount = pkg != null ? (pkg.TotalBasePrice * (pkg.Discount ?? 0) / 100) : 0m;

        int incBags = pkg?.IncludedBaggageCount ?? 2;
        decimal extraBagPrice = pkg?.ExtraBaggagePrice ?? 25m;
        int incComps = pkg?.IncludedCompanionsCount ?? 1;
        decimal extraCompPrice = pkg?.ExtraCompanionPrice ?? 20m;

        int totalBags = draft.TotalBaggageCount;
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
    // STEP 6 — confirm
    // ===================================================================
    public async Task<ConfirmOrderResponse> ConfirmOrderAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetBagTrackingDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Session not found" };

        if (!draft.BaggageValidated)
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "Baggage validation step must be completed first" };
        if (!draft.ScannedBags.Any())
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "At least one bag must be scanned" };

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var invoiceDto = await GetInvoiceAsync(customerId, cancellationToken);

                var pkg = await _context.Packages.FirstOrDefaultAsync(
                    p => p.PackageName == PackageNames.TrackingBaggage, cancellationToken);

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

                // Order (no locations in Bag Tracking, but DB requires them)
                var pickupLocation = new Domain.Entities.Location
                {
                    StreetAddress = "N/A",
                    City = draft.FlightInfo.DepartureAirport ?? "N/A",
                    State = string.Empty,
                    Country = string.Empty,
                    PostalCode = string.Empty,
                    GpsLatitude = 0,
                    GpsLongitude = 0,
                    LocationType = LocationType.Pickup,
                    CustomerId = customerId
                };

                var deliveryLocation = new Domain.Entities.Location
                {
                    StreetAddress = "N/A",
                    City = draft.FlightInfo.ArrivalAirport ?? "N/A",
                    State = string.Empty,
                    Country = string.Empty,
                    PostalCode = string.Empty,
                    GpsLatitude = 0,
                    GpsLongitude = 0,
                    LocationType = LocationType.Delivery,
                    CustomerId = customerId
                };

                _context.Locations.Add(pickupLocation);
                _context.Locations.Add(deliveryLocation);
                await _context.SaveChangesAsync(cancellationToken);

                var order = new Domain.Entities.Order
                {
                    CustomerId = customerId,
                    FlightId = flight.FlightId,
                    PackageId = pkg?.PackageId ?? 1,
                    OrderStatus = OrderStatus.Pending,
                    TicketNumber = draft.TicketNumber,
                    ExtraCompanionsCount = invoiceDto.Breakdown.CompanionDetails.ExtraCompanions,
                    ExtraCompanionsFee = invoiceDto.Breakdown.CompanionDetails.ExtraCompanionsFee,
                    TotalBaggageCount = invoiceDto.Breakdown.BaggageDetails.TotalBags,
                    ExtraBaggageCount = invoiceDto.Breakdown.BaggageDetails.ExtraBags,
                    ExtraBaggageFee = invoiceDto.Breakdown.BaggageDetails.ExtraBaggageFee,
                    TotalAmount = invoiceDto.Breakdown.TotalAmount,
                    PickupDate = draft.FlightInfo.DepartureTimeUtc.Date,
                    PickupTimeSlot = "N/A",
                    DeliveryDate = draft.FlightInfo.DepartureTimeUtc.Date,
                    DeliveryTimeSlot = "N/A",
                    PickupLocationId = pickupLocation.LocationId,
                    DeliveryLocationId = deliveryLocation.LocationId
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

                    _context.OrderCompanions.Add(new Domain.Entities.OrderCompanion
                    {
                        OrderId = order.OrderId,
                        CompanionId = companionEntity.CompanionId,
                        TicketNumber = comp.TicketNumber
                    });
                }

                // Build tagNumber → ticketNumber map
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

                // Baggages + BaggagePhotos
                foreach (var scannedBag in draft.ScannedBags)
                {
                    var ownerTicket = bagTagOwnerMap.GetValueOrDefault(scannedBag.TagNumber, draft.TicketNumber);

                    var baggage = new Domain.Entities.Baggage
                    {
                        OrderId = order.OrderId,
                        CustomerId = customerId,
                        BaggageNumber = scannedBag.TagNumber,
                        TotalWeight = scannedBag.WeightKg,
                        Destination = scannedBag.Destination
                    };

                    if (ownerTicket == draft.TicketNumber)
                    {
                        baggage.OwnerType = BaggageOwnerType.Customer;
                    }
                    else
                    {
                        var comp = draft.Companions.FirstOrDefault(c => c.TicketNumber == ownerTicket);
                        if (comp != null && companionIdMap.TryGetValue(comp.PassportNumber, out int compId))
                        {
                            baggage.CompanionId = compId;
                            baggage.OwnerType = BaggageOwnerType.Companion;
                        }
                        else
                        {
                            baggage.OwnerType = BaggageOwnerType.Customer;
                        }
                    }

                    _context.Baggages.Add(baggage);
                    await _context.SaveChangesAsync(cancellationToken);

                    foreach (var photoUrl in scannedBag.Photos)
                    {
                        _context.BaggagePhotos.Add(new Domain.Entities.BaggagePhoto
                        {
                            ImagePath = photoUrl,
                            BaggageId = baggage.BaggageId,
                            CaptureTimestamp = DateTime.UtcNow
                        });
                    }
                }

                // OrderService
                var packageServices = await _context.PackageServices
                    .Where(ps => ps.PackageId == order.PackageId)
                    .Include(ps => ps.Service)
                    .ToListAsync(cancellationToken);

                foreach (var packageService in packageServices)
                {
                    _context.OrderServices.Add(new Domain.Entities.OrderService
                    {
                        OrderId = order.OrderId,
                        PackageServiceId = packageService.PackageServiceId,
                        ServiceStatus = ServiceStatus.Pending,
                        ScheduledStartTime = draft.FlightInfo.DepartureTimeUtc,
                        ScheduledEndTime = draft.FlightInfo.DepartureTimeUtc.AddHours(2),
                        AssignedEmployeeId = null,
                        AssignedAt = null
                    });
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await _draftOrderService.RemoveBagTrackingDraftAsync(customerId.ToString(), cancellationToken);

                // Customer Notification — Order Confirmed
                _context.Notifications.Add(new Notification
                {
                    UserId = customerId,
                    UserType = UserType.Customer,
                    NotificationType = NotificationType.OrderUpdated,
                    Title = "Your order has been confirmed",
                    Message = $"Order #{order.OrderId} for Bag Tracking has been placed successfully",
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = order.OrderId
                });
                await _context.SaveChangesAsync(cancellationToken);

                await _pusher.PushToCustomerAsync(
                    customerId,
                    "Your order has been confirmed",
                    $"Order #{order.OrderId} for Bag Tracking has been placed successfully",
                    "OrderConfirmed",
                    order.OrderId);

                return new ConfirmOrderResponse
                {
                    IsValid = true,
                    Success = true,
                    OrderId = order.OrderId,
                    OrderNumber = $"LTS-{DateTime.UtcNow.Year}-{order.OrderId}",
                    TotalPaid = invoiceDto.Breakdown.TotalAmount
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
    // HELPERS
    // ===================================================================
    private async Task ValidateTicketNotUsedAsync(string ticketNumber, string packageName, CancellationToken cancellationToken)
    {
        var package = await _context.Packages
            .FirstOrDefaultAsync(p => p.PackageName == packageName, cancellationToken)
            ?? throw new InvalidOperationException($"Package {packageName} not found in DB");

        var isTicketUsed = await _context.Orders
            .AnyAsync(o => o.TicketNumber == ticketNumber
                        && o.PackageId == package.PackageId
                        && o.OrderStatus != OrderStatus.Cancelled, cancellationToken);

        if (isTicketUsed)
            throw new InvalidOperationException($"This ticket is already used in {packageName} service.");
    }
}
