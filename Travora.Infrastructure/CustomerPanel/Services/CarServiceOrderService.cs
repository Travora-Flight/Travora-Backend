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

    public CarServiceOrderService(
        ApplicationDbContext context,
        IAirlineService airlineService,
        ICloudinaryService cloudinaryService,
        IDraftOrderService draftOrderService,
        IGeocodingService geocodingService,
        INotificationPusher pusher)
    {
        _context = context;
        _airlineService = airlineService;
        _cloudinaryService = cloudinaryService;
        _draftOrderService = draftOrderService;
        _geocodingService = geocodingService;
        _pusher = pusher;
    }

    // ===================================================================
    // STEP 1 — التحقق من بيانات الرحلة + نوع الخدمة
    // ===================================================================
    public async Task<CarServiceValidateFlightResponse> ValidateFlightAsync(
        int customerId, CarServiceValidateFlightRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);

        if (customer == null)
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = "العميل غير موجود" };
        if (string.IsNullOrEmpty(customer.PassportNumber))
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = "رقم الجواز غير موجود، يرجى استكمال البيانات" };
        if (customer.AccountStatus != CustomerAccountStatus.Verified)
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = "يجب توثيق حسابك لاستخدام الخدمة" };

        // التحقق من أن التذكرة مش مستخدمة في نفس الباكيج
        var packageName = request.ServiceType == CarServiceType.DeliveryToAirport
            ? PackageNames.CarServiceToAirport
            : PackageNames.CarServiceFromAirport;
        try
        {
            await ValidateTicketNotUsedAsync(request.TicketNumber, packageName, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = ex.Message };
        }

        // استدعاء Airline API
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
                : "بيانات الرحلة أو التذكرة غير صحيحة";
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = errorMsg };
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
            return new CarServiceValidateFlightResponse { IsValid = false, ErrorMessage = "لا يمكن الحجز قبل أقل من 12 ساعة من الإقلاع" };

        var bookingDeadlineUtc = departure.AddHours(-12);

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
    // STEP 2 — إضافة مرافقين
    // ===================================================================
    public async Task<ValidateCompanionResponse> ValidateCompanionAsync(
        int customerId, ValidateCompanionRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "الجلسة انتهت أو غير موجودة، يرجى إعادة البدء" };

        if (request.PassportNumber == draft.PassengerInfo?.PassportNumber)
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "لا يمكنك إضافة نفسك كمرافق" };

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
                : "بيانات المرافق غير صحيحة";
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = errorMsg };
        }

        if (flightData.FlightNumber != draft.FlightInfo.FlightNumber)
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "المرافق ليس على نفس الرحلة" };

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
            await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);
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
    // STEP 2.5 — التحقق من إجمالي الشنط
    // ===================================================================
    public async Task<ValidateBaggageResponse> ValidateBaggageAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ValidateBaggageResponse { IsValid = false, ErrorMessage = "الجلسة غير موجودة" };

        // استدعاء baggage-check بالتوازي
        var tasks = new List<(string TicketNumber, Task<AirlineBaggageCheckResponse> Task)>
        {
            (draft.TicketNumber, _airlineService.GetBaggageCountAsync(draft.TicketNumber, cancellationToken))
        };

        foreach (var comp in draft.Companions)
            tasks.Add((comp.TicketNumber, _airlineService.GetBaggageCountAsync(comp.TicketNumber, cancellationToken)));

        await Task.WhenAll(tasks.Select(t => t.Task));

        // بناء الـ breakdown من الـ response الحقيقية
        var breakdown = new List<BaggageBreakdown>();
        int totalFromAirline = 0;

        foreach (var t in tasks)
        {
            var result = t.Task.Result;
            // لو فيه tickets في الـ response، استخدمها
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
            totalFromAirline += result.TotalBaggageCount;
        }

        if (draft.BaggageCount != totalFromAirline)
        {
            return new ValidateBaggageResponse
            {
                IsValid = false,
                ErrorCode = "BaggageCountMismatch",
                ErrorMessage = "عدد الشنط المدخل لا يطابق السجل لدى شركة الطيران",
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

        await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new ValidateBaggageResponse
        {
            IsValid = true,
            TotalBaggageCount = totalFromAirline,
            Breakdown = breakdown
        };
    }

    // ===================================================================
    // STEP 3 — العنوان (Reverse Geocoding)
    // ===================================================================
    public async Task<ResolveLocationResponse> ResolveLocationAsync(
        int customerId, CarServiceResolveLocationRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            return new ResolveLocationResponse { IsValid = false, ErrorMessage = "الجلسة غير موجودة" }; // using return new ErrorMessage since return type changed

        if (!draft.BaggageValidated)
            return new ResolveLocationResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة التحقق من الشنط أولاً" };

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
    // STEP 4 — اختيار المواعيد
    // ===================================================================
    public async Task<AvailableSlotsResponse> GetAvailableSlotsAsync(
        int customerId, DateTime date, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "الجلسة غير موجودة، يرجى البدء من الخطوة الأولى" };

        if (string.IsNullOrEmpty(draft.LocationFormattedAddress))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة تحديد الموقع أولاً" };

        var flightDate = draft.FlightInfo.DepartureTimeUtc.Date;
        var today = DateTime.UtcNow.Date;

        if (date.Date < today)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "لا يمكن اختيار يوم في الماضي" };
        if (date.Date > flightDate)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "لا يمكن الحجز بعد يوم الرحلة" };

        var response = new AvailableSlotsResponse();
        TimeSpan? cutoffTimeSpan = null;

        if (date.Date == flightDate)
        {
            var cutoffUtc = draft.FlightInfo.DepartureTimeUtc.AddHours(-12);
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
    // STEP 5 — الشنط (delivery_from_airport فقط) — بيانات حقيقية من baggageTags
    // ===================================================================
    public async Task<MyBagsResponse> GetMyBagsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new MyBagsResponse { IsValid = false, ErrorMessage = "الجلسة غير موجودة" };

        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new MyBagsResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة اختيار الموعد أولاً" };

        if (draft.ServiceType != CarServiceType.DeliveryFromAirport)
            return new MyBagsResponse { IsValid = false, ErrorMessage = "هذه الخطوة متاحة فقط لخدمة delivery From Airport" };

        // جمع كل الـ ticketNumbers
        var ticketNumbers = new List<string> { draft.TicketNumber };
        ticketNumbers.AddRange(draft.Companions.Select(c => c.TicketNumber));

        // استدعاء baggage-check بالتوازي
        var tasks = ticketNumbers.Select(tn => new
        {
            TicketNumber = tn,
            Task = _airlineService.GetBaggageCountAsync(tn, cancellationToken)
        }).ToList();

        await Task.WhenAll(tasks.Select(t => t.Task));

        var allBags = new List<BagItem>();
        foreach (var t in tasks)
        {
            var result = t.Task.Result;
            // استخدام baggageTags الحقيقية من الـ response
            if (result.Tickets != null)
            {
                foreach (var ticket in result.Tickets)
                {
                    if (ticket.BaggageTags != null)
                    {
                        foreach (var tag in ticket.BaggageTags)
                        {
                            allBags.Add(new BagItem
                            {
                                TagNumber = tag.TagNumber,
                                WeightKg = tag.WeightKg,
                                Journey = $"{tag.Origin ?? draft.FlightInfo.DepartureAirport} → {tag.Destination ?? draft.FlightInfo.ArrivalAirport}",
                                Gate = tag.Gate ?? "N/A",
                                Terminal = tag.Terminal ?? "N/A",
                                TicketNumber = ticket.TicketNumber
                            });
                        }
                    }
                }
            }
        }

        return new MyBagsResponse { Bags = allBags };
    }

    // ===================================================================
    // STEP 5.5 — اختيار الشنط
    // ===================================================================
    public async Task SelectBagsAsync(int customerId, SelectBagsRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null)
            throw new Exception("الجلسة غير موجودة");

        if (string.IsNullOrEmpty(draft.SelectedSlot))
            throw new Exception("يجب إكمال خطوة اختيار الموعد أولاً");

        if (draft.ServiceType != CarServiceType.DeliveryFromAirport)
            throw new Exception("هذه الخطوة متاحة فقط لخدمة delivery From Airport");

        if (request.SelectedTagNumbers == null || !request.SelectedTagNumbers.Any())
            throw new Exception("يجب اختيار شنطة واحدة على الأقل");

        // امنع العميل من اختيار نفس الـ tag مرتين
        if (request.SelectedTagNumbers.Distinct().Count() != request.SelectedTagNumbers.Count)
            throw new Exception("لا يمكن اختيار نفس الشنطة مرتين");

        draft.SelectedBagTags = request.SelectedTagNumbers;
        await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);
    }

    // ===================================================================
    // STEP 6 — الفاتورة
    // ===================================================================
    public async Task<InvoiceResponse> GetInvoiceAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new InvoiceResponse { IsValid = false, ErrorMessage = "الجلسة غير موجودة" };

        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new InvoiceResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة اختيار الموعد أولاً" };

        if (draft.ServiceType == CarServiceType.DeliveryFromAirport && !draft.SelectedBagTags.Any())
            return new InvoiceResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة اختيار الشنط المراد توصيلها أولاً" };

        var packageName = draft.ServiceType == CarServiceType.DeliveryToAirport
            ? PackageNames.CarServiceToAirport
            : PackageNames.CarServiceFromAirport;
        var pkg = await _context.Packages.FirstOrDefaultAsync(
            p => p.PackageName == packageName, cancellationToken);

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
    // STEP 7 — تأكيد الأوردر
    // ===================================================================
    public async Task<ConfirmOrderResponse> ConfirmOrderAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "الجلسة غير موجودة" };

        if (!draft.BaggageValidated)
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "يجب إكمال خطوة التحقق من الشنط أولاً" };
        if (string.IsNullOrEmpty(draft.LocationFormattedAddress))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "يجب إكمال خطوة تحديد الموقع أولاً" };
        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "يجب إكمال خطوة اختيار الموعد أولاً" };
        if (draft.ServiceType == CarServiceType.DeliveryFromAirport && !draft.SelectedBagTags.Any())
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "يجب إكمال خطوة اختيار الشنط المراد توصيلها أولاً" };

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var invoiceDto = await GetInvoiceAsync(customerId, cancellationToken);

                var packageName = draft.ServiceType == CarServiceType.DeliveryToAirport
                    ? PackageNames.CarServiceToAirport
                    : PackageNames.CarServiceFromAirport;
                var pkg = await _context.Packages.FirstOrDefaultAsync(
                    p => p.PackageName == packageName, cancellationToken);

                // Flight
                string flightNo = draft.FlightInfo.FlightNumber;
                var flight = await _context.Flights.FirstOrDefaultAsync(f => f.FlightNumber == flightNo, cancellationToken);
                if (flight == null)
                {
                    flight = new Domain.Entities.Flight
                    {
                        FlightNumber = flightNo,
                        AirlineIcaoCode = (draft.FlightInfo.AirlineIcaoCode ?? "MS").Trim(),
                        AirlineName = draft.FlightInfo.AirlineName ?? string.Empty,
                        DepartureIataCode = (draft.FlightInfo.DepartureAirport ?? "CAI").Trim().Substring(0, Math.Min(35, (draft.FlightInfo.DepartureAirport ?? "CAI").Trim().Length)),
                        ArrivalIataCode = (draft.FlightInfo.ArrivalAirport ?? "JFK").Trim().Substring(0, Math.Min(35, (draft.FlightInfo.ArrivalAirport ?? "JFK").Trim().Length)),
                        DepartureTerminal = draft.FlightInfo.Terminal,
                        DepartureGate = draft.FlightInfo.Gate,
                        ScheduledDepartureTime = draft.FlightInfo.DepartureTimeUtc,
                        ScheduledArrivalTime = draft.FlightInfo.ArrivalTimeUtc ?? draft.FlightInfo.DepartureTimeUtc.AddHours(4),
                        FlightStatus = FlightStatus.Scheduled,
                        DataSource = "AirlineSimulation"
                    };
                    _context.Flights.Add(flight);
                    await _context.SaveChangesAsync(cancellationToken);
                }

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
                var slotParts = draft.SelectedSlot!.Split('-');
                var slotStart = TimeSpan.Parse(slotParts[0]);
                var slotEnd = slotParts[1] == "24:00" ? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)) : TimeSpan.Parse(slotParts[1]);
                var slotDate = draft.SelectedSlotDate ?? draft.FlightInfo.DepartureTimeUtc.Date;

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
                        ? slotDate 
                        : (draft.FlightInfo.ArrivalTimeUtc?.Date ?? draft.FlightInfo.DepartureTimeUtc.Date),
                    PickupTimeSlot = draft.ServiceType == CarServiceType.DeliveryToAirport 
                        ? (draft.SelectedSlot ?? "10:00-12:00") 
                        : "N/A",
                    DeliveryDate = draft.ServiceType == CarServiceType.DeliveryToAirport 
                        ? draft.FlightInfo.DepartureTimeUtc.Date 
                        : slotDate,
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

                // OrderService
                var packageServices = await _context.PackageServices
                    .Where(ps => ps.PackageId == order.PackageId)
                    .Include(ps => ps.Service)
                    .ToListAsync(cancellationToken);

                foreach (var packageService in packageServices)
                {
                    DateTime scheduledStart = slotDate.Date + slotStart;
                    DateTime scheduledEnd = slotDate.Date + slotEnd;

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

                return new ConfirmOrderResponse
                {
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
    // STEP 8 — تعيين سائق بعد الدفع + إشعار العميل
    // ===================================================================
    public async Task AssignEmployeesAfterPaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { orderId }, cancellationToken);

        var servicesToAssign = await _context.OrderServices
            .Where(os => os.OrderId == orderId && os.ServiceStatus == ServiceStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var service in servicesToAssign)
        {
            var driver = await FindAvailableDriverAsync(service.ScheduledStartTime, service.ScheduledEndTime, cancellationToken);
            if (driver != null)
            {
                service.AssignedEmployeeId = driver.EmployeeId;
                service.AssignedAt = DateTime.UtcNow;
                service.ServiceStatus = ServiceStatus.Assigned;

                _context.Notifications.Add(new Domain.Entities.Notification
                {
                    UserId = driver.EmployeeId,
                    UserType = UserType.Employee,
                    NotificationType = NotificationType.OrderUpdated,
                    Title = "تم تعيينك على طلب جديد (Car Service)",
                    Message = $"طلب توصيل - الموعد: {service.ScheduledStartTime:dd/MM hh:mm tt}",
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = orderId
                });
            }
        }

        // إشعار العميل
        if (order != null)
        {
            _context.Notifications.Add(new Domain.Entities.Notification
            {
                UserId = order.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.OrderUpdated,
                Title = "تم تأكيد طلبك",
                Message = "تم تعيين سائق لطلبك بنجاح",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = orderId
            });

            await _pusher.PushToCustomerAsync(
                order.CustomerId,
                "تم تأكيد طلبك",
                "تم تعيين سائق لطلبك بنجاح",
                "OrderConfirmed",
                orderId);
        }

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

    private bool HasConflict(Domain.Entities.Employee driver, DateTime date, TimeSpan slotStart, TimeSpan slotEnd)
    {
        return driver.AssignedOrderServices.Any(os =>
            os.ScheduledStartTime.Date == date &&
            os.ScheduledStartTime.TimeOfDay < slotEnd &&
            os.ScheduledEndTime.TimeOfDay > slotStart
        );
    }

    private async Task<Domain.Entities.Employee?> FindAvailableDriverAsync(
        DateTime scheduledStart, DateTime scheduledEnd, CancellationToken cancellationToken)
    {
        var slotStart = scheduledStart.TimeOfDay;
        var slotEnd = scheduledEnd.TimeOfDay;
        var date = scheduledStart.Date;

        var drivers = await _context.Employees
            .Where(e => e.JobRole == JobRole.Driver && e.IsActive && !e.IsDeleted)
            .Include(e => e.AssignedOrderServices)
            .ToListAsync(cancellationToken);

        return drivers.FirstOrDefault(d =>
            IsShiftCovering(d.ShiftType, slotStart, slotEnd) &&
            !HasConflict(d, date, slotStart, slotEnd));
    }

    private async Task ValidateTicketNotUsedAsync(string ticketNumber, string packageName, CancellationToken cancellationToken)
    {
        var package = await _context.Packages
            .FirstOrDefaultAsync(p => p.PackageName == packageName, cancellationToken)
            ?? throw new InvalidOperationException($"باكيج {packageName} مش موجود في الـ DB");

        var isTicketUsed = await _context.Orders
            .AnyAsync(o => o.TicketNumber == ticketNumber
                        && o.PackageId == package.PackageId
                        && o.OrderStatus != OrderStatus.Cancelled, cancellationToken);

        if (isTicketUsed)
            throw new InvalidOperationException($"هذه التذكرة مستخدمة بالفعل في خدمة {packageName}.");
    }
}
