using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.External.Airline;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.Interfaces.External;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Customer;
using Travora.Domain.Constants;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.CustomerPanel.Services;

public class DoorToDoorOrderService : IDoorToDoorOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IAirlineService _airlineService;
    private readonly ICloudinaryService _cloudinaryService;
    private readonly IDraftOrderService _draftOrderService;
    private readonly IGeocodingService _geocodingService;

    public DoorToDoorOrderService(
        ApplicationDbContext context,
        IAirlineService airlineService,
        ICloudinaryService cloudinaryService,
        IDraftOrderService draftOrderService,
        IGeocodingService geocodingService)
    {
        _context = context;
        _airlineService = airlineService;
        _cloudinaryService = cloudinaryService;
        _draftOrderService = draftOrderService;
        _geocodingService = geocodingService;
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

        // 1.5 Prevent duplicate ticket usage for Door To Door
        try
        {
            await ValidateTicketNotUsedAsync(request.TicketNumber, "Door To Door", cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = ex.Message };
        }

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
            return new ValidateFlightResponse { IsValid = false, ErrorMessage = "لا يمكن الحجز قبل أقل من 12 ساعة من الإقلاع" };
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

        if (request.PassportNumber == draft.PassengerInfo?.PassportNumber)
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "لا يمكنك إضافة نفسك كمرافق" };

        // 2. Validate companion ticket with airline API
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
                : "Invalid ticket details for this companion.";
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = errorMsg };
        }

        // 3. Ensure the companion is on the same flight (already filtered by sending FlightNumber above, 
        //    but double checking if the simulation ignores it)
        if (flightData.FlightNumber != draft.FlightInfo.FlightNumber)
        {
            return new ValidateCompanionResponse { IsValid = false, ErrorMessage = "المرافق ليس على نفس الرحلة" };
        }

        // 4. Upload passport image
        string defaultImageUrl = "https://res.cloudinary.com/travora/image/upload/vdefault/companion.jpg";
        string imageUrl = defaultImageUrl;
        if (request.PassportImage != null && request.PassportImage.Length > 0)
        {
            using var stream = request.PassportImage.OpenReadStream();
            var uploadResult = await _cloudinaryService.UploadFileAsync(stream, request.PassportImage.FileName, "travora/companions");
            if (!string.IsNullOrEmpty(uploadResult))
            {
                imageUrl = uploadResult;
            }
        }

        // 5. Save to draft
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

        // Ensure we don't add the same companion twice (by passport)
        if (!draft.Companions.Any(c => c.PassportNumber == request.PassportNumber))
        {
            draft.Companions.Add(newCompanion);
            // Reset TTL when modifying
            await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);
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

    public async Task<ValidateBaggageResponse> ValidateBaggageAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ValidateBaggageResponse { IsValid = false, ErrorMessage = "Draft order not found" };

        var tasks = new List<(string TicketNumber, Task<AirlineBaggageCheckResponse> Task)>();

        // العميل الأساسي
        tasks.Add((draft.TicketNumber, _airlineService.GetBaggageCountAsync(draft.TicketNumber, cancellationToken)));

        // المرافقين
        foreach (var comp in draft.Companions)
        {
            tasks.Add((comp.TicketNumber, _airlineService.GetBaggageCountAsync(comp.TicketNumber, cancellationToken)));
        }

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
                ErrorMessage = "عدد الشنط المدخل لا يطابق السجل لدى شركة الطيران",
                Expected = totalFromAirline,
                Actual = draft.BaggageCount,
                TotalBaggageCount = totalFromAirline,
                Breakdown = breakdown
            };
        }

        draft.TotalBaggageCount = totalFromAirline;
        draft.BaggageValidated = true;

        // توزيع الشنط على المرافقين
        foreach (var comp in draft.Companions)
        {
            var companionBags = breakdown.FirstOrDefault(b => b.TicketNumber == comp.TicketNumber);
            comp.BaggageCount = companionBags?.BaggageCount ?? 0;
        }

        await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

        return new ValidateBaggageResponse
        {
            IsValid = true,
            TotalBaggageCount = totalFromAirline,
            Breakdown = breakdown
        };
    }

    public async Task<ResolveLocationResponse> ResolveLocationAsync(int customerId, ResolveLocationRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new ResolveLocationResponse { IsValid = false, ErrorMessage = "الجلسة غير موجودة" };

        if (string.Equals(request.LocationType, "delivery", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(draft.PickupFormattedAddress))
                return new ResolveLocationResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة تحديد موقع الاستلام أولاً" };
        }
        else // pickup
        {
            if (!draft.BaggageValidated)
                return new ResolveLocationResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة التحقق من الشنط أولاً" };
        }

        var result = await _geocodingService.ReverseGeocodeAsync(request.Latitude, request.Longitude, cancellationToken);
        
        var response = new ResolveLocationResponse
        {
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

    public async Task<AvailableSlotsResponse> GetAvailableSlotsAsync(int customerId, DateTime date, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "Draft order not found. Please start from Step 1." };

        if (string.IsNullOrEmpty(draft.PickupFormattedAddress))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة تحديد موقع الاستلام أولاً" };

        if (string.IsNullOrEmpty(draft.DeliveryFormattedAddress))
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة تحديد موقع التسليم أولاً" };

        var flightDate = draft.FlightInfo.DepartureTimeUtc.Date;
        var today = DateTime.UtcNow.Date;
        if (date.Date == null)
            return new AvailableSlotsResponse { IsValid = false, ErrorMessage = "يرجى اختيار يوم" };

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
            .Where(e => e.JobRole == Domain.Enums.JobRole.Driver && e.IsActive && !e.IsDeleted)
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

            // Cutoff check
            if (cutoffTimeSpan.HasValue && end > cutoffTimeSpan.Value)
            {
                isAvailable = false;
            }
            else
            {
                // Driver availability check
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

        if (string.IsNullOrEmpty(draft.SelectedDeliverySlot))
            return new SetCustomsTypeResponse { Success = false, ErrorMessage = "يجب إكمال خطوة اختيار موعد التسليم أولاً" };

        string normalizedType = request.CustomsType?.Trim().ToLower().Replace("_", "").Replace(" ", "") ?? "";
        
        if (normalizedType == "greenfield")
        {
            draft.CustomsType = "GreenField";
            await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);
            return new SetCustomsTypeResponse 
            { 
                Success = true, 
                CustomsType = "GreenField", 
                Message = "تم اختيار الخط الأخضر، لا توجد رسوم جمركية" 
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
                Message = "تم اختيار الخط الأحمر، يرجى إضافة المنتجات الجمركية" 
            };
        }
        else
        {
            return new SetCustomsTypeResponse { Success = false, ErrorMessage = "نوع الجمارك غير صحيح" };
        }
    }

    public async Task<CustomsLookupResponse> LookupCustomsProductAsync(string productName, CancellationToken cancellationToken = default)
    {
        var result = await _airlineService.LookupCustomsProductAsync(productName, cancellationToken);
        if (!result.Found || result.Product == null)
            return new CustomsLookupResponse { Found = false, Message = "المنتج غير موجود، يرجى إدخال البيانات يدوياً" };

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
            return new AddCustomsItemResponse { Success = false, ErrorMessage = "يجب إكمال خطوة تحديد نوع الجمارك أولاً" };

        if (draft.CustomsType != "RedField")
            return new AddCustomsItemResponse { Success = false, ErrorMessage = "لا يمكن إضافة منتجات في الخط الأخضر" };

        // جيب الـ rate تلقائياً من الـ API
        var lookupResult = await _airlineService.LookupCustomsProductAsync(request.ItemDescription, cancellationToken);
        decimal customsRate = lookupResult.Found && lookupResult.Product != null
            ? lookupResult.Product.CustomsRate
            : 0m;

        string invoiceUrl = string.Empty;
        if (request.PurchaseInvoice != null && request.PurchaseInvoice.Length > 0)
        {
            using var stream = request.PurchaseInvoice.OpenReadStream();
            var uploadResult = await _cloudinaryService.UploadFileAsync(stream, request.PurchaseInvoice.FileName, "travora/customs-invoices");
            if (!string.IsNullOrEmpty(uploadResult))
                invoiceUrl = uploadResult;
        }

        var item = new DraftCustomsItem
        {
            ItemDescription = request.ItemDescription,
            ItemType = lookupResult.Found && lookupResult.Product != null ? lookupResult.Product.Category ?? "Other" : "Other",
            DeclaredValue = request.DeclaredValue,
            Quantity = request.Quantity,
            CustomsRatePercentage = customsRate,
            PurchaseInvoiceUrl = invoiceUrl
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

        if (string.IsNullOrEmpty(draft.SelectedDeliverySlot))
            return new InvoiceResponse { IsValid = false, ErrorMessage = "يجب إكمال خطوة اختيار موعد التسليم أولاً" };

        var pkg = await _context.Packages.FirstOrDefaultAsync(p => p.PackageName == PackageNames.DoorToDoor, cancellationToken);
        
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
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "يجب إكمال خطوة التحقق من الشنط أولاً" };
        if (string.IsNullOrEmpty(draft.PickupFormattedAddress))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "يجب إكمال خطوة تحديد موقع الاستلام أولاً" };
        if (string.IsNullOrEmpty(draft.SelectedSlot))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "يجب إكمال خطوة اختيار موعد الاستلام أولاً" };
        if (string.IsNullOrEmpty(draft.DeliveryFormattedAddress))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "يجب إكمال خطوة تحديد موقع التسليم أولاً" };
        if (string.IsNullOrEmpty(draft.SelectedDeliverySlot))
            return new ConfirmOrderResponse { Success = false, ErrorMessage = "يجب إكمال خطوة اختيار موعد التسليم أولاً" };

        var strategy = _context.Database.CreateExecutionStrategy();
        
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var invoiceDto = await GetInvoiceAsync(customerId, cancellationToken);
                var pkg = await _context.Packages.FirstOrDefaultAsync(p => 
                    p.PackageName == PackageNames.DoorToDoor, cancellationToken);

                string flightNo = draft.FlightInfo.FlightNumber;

                // 1) استخرج IATA codes من draft
                var depIata = (draft.FlightInfo.DepartureIataCode
                    ?? draft.FlightInfo.DepartureAirport
                    ?? "").Trim();
                var arrIata = (draft.FlightInfo.ArrivalIataCode
                    ?? draft.FlightInfo.ArrivalAirport
                    ?? "").Trim();

                // 2) ابحث عن الرحلة بالـ FlightNumber
                var flight = await _context.Flights
                    .FirstOrDefaultAsync(f => f.FlightNumber == flightNo, cancellationToken);

                // 3) لو جديدة → أنشئها
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
                // 4) لو موجودة → حدّث البيانات
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

                // 5) اربط بالـ Airport من جدول Airports
                var departureAirport = await _context.Airports
                    .FirstOrDefaultAsync(a => a.CodeIataAirport == depIata, cancellationToken);
                if (departureAirport != null)
                    flight.DepartureAirportId = departureAirport.AirportId;

                var arrivalAirport = await _context.Airports
                    .FirstOrDefaultAsync(a => a.CodeIataAirport == arrIata, cancellationToken);
                if (arrivalAirport != null)
                    flight.ArrivalAirportId = arrivalAirport.AirportId;

                // 6) احفظ
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

                        _context.CustomsItems.Add(new Domain.Entities.CustomsItem
                        {
                            CustomsId = declaration.CustomsId,
                            ItemDescription = item.ItemDescription,
                            ItemType = parsedItemType,
                            DeclaredValue = item.DeclaredValue,
                            Quantity = item.Quantity,
                            TotalValue = item.TotalValue,
                            CustomsRatePercentage = item.CustomsRatePercentage,
                            TotalCustomsValue = item.TotalCustomsValue
                        });
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

                    if (packageService.ExecutionPhase == Domain.Enums.ExecutionPhase.Pickup)
                    {
                        var slotParts = draft.SelectedSlot!.Split('-');
                        var slotStart = TimeSpan.Parse(slotParts[0]);
                        var slotEnd = slotParts[1] == "24:00" ? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)) : TimeSpan.Parse(slotParts[1]);
                        scheduledStart = draft.SelectedSlotDate!.Value.Date + slotStart;
                        scheduledEnd = draft.SelectedSlotDate!.Value.Date + slotEnd;

                        // Employee gets assigned AFTER payment, so we leave it as Pending for now
                        status = Domain.Enums.ServiceStatus.Pending;
                    }
                    else if (packageService.ExecutionPhase == Domain.Enums.ExecutionPhase.AirportCheckin)
                    {
                        // وقت تقريبي — هيتحدد فعلياً لما Driver يعمل Complete على Pickup
                        scheduledStart = draft.FlightInfo.DepartureTimeUtc.AddHours(-3);
                        scheduledEnd = draft.FlightInfo.DepartureTimeUtc.AddHours(-1);
                        // مش بيتعمله assign — بيفضل Pending
                    }
                    else // Delivery
                    {
                        var slotParts = draft.SelectedDeliverySlot!.Split('-');
                        var slotStart = TimeSpan.Parse(slotParts[0]);
                        var slotEnd = slotParts[1] == "24:00" ? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)) : TimeSpan.Parse(slotParts[1]);
                        scheduledStart = draft.SelectedDeliverySlotDate!.Value.Date + slotStart;
                        scheduledEnd = draft.SelectedDeliverySlotDate!.Value.Date + slotEnd;
                        // هيتعمله assign أوتوماتيك لما BaggageHandler يكمل AirportCheckin
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

    public async Task<AvailableSlotsResponse> GetAvailableDeliverySlotsAsync(int customerId, DateTime date, CancellationToken cancellationToken = default)
    {
        var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
        if (draft == null || draft.FlightInfo == null)
            throw new Exception("Draft order not found. Please start from Step 1.");

        if (string.IsNullOrEmpty(draft.SelectedSlot))
            throw new Exception("يجب إكمال خطوة اختيار موعد الاستلام أولاً");

        var arrivalTimeUtc = draft.FlightInfo.ArrivalTimeUtc ?? draft.FlightInfo.DepartureTimeUtc.AddHours(4);
        var arrivalDate = arrivalTimeUtc.Date;
        var maxDeliveryDate = arrivalDate.AddDays(2);
        var earliestDelivery = arrivalTimeUtc.AddHours(1);

        if (date.Date < arrivalDate)
            throw new Exception("لا يمكن اختيار يوم قبل يوم الوصول");

        if (date.Date > maxDeliveryDate)
            throw new Exception("لا يمكن الحجز بعد يومين من تاريخ الوصول");

        var response = new AvailableSlotsResponse();
        TimeSpan? earliestTimeSpan = null;

        if (date.Date == arrivalDate)
        {
            earliestTimeSpan = earliestDelivery.TimeOfDay;
            response.Note = $"أقرب موعد تسليم متاح بعد {earliestTimeSpan.Value.ToString(@"hh\:mm")}";
        }

        var allDrivers = await _context.Employees
            .Where(e => e.JobRole == Domain.Enums.JobRole.Driver && e.IsActive && !e.IsDeleted)
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

            // لو نفس يوم الوصول — السلوت لازم يبدأ بعد الوصول بساعة
            if (earliestTimeSpan.HasValue && start < earliestTimeSpan.Value)
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
            .Where(e => e.JobRole == Domain.Enums.JobRole.Driver
                     && e.IsActive
                     && !e.IsDeleted)
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
                    Title = "تم تعيينك على طلب جديد (مؤكد الدفع)",
                    Message = $"طلب استلام شنط - الموعد: {service.ScheduledStartTime:dd/MM hh:mm tt}",
                    NotificationChannel = Domain.Enums.NotificationChannel.InApp,
                    OrderId = orderId
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateTicketNotUsedAsync(string ticketNumber, string packageName, CancellationToken cancellationToken)
    {
        var package = await _context.Packages
            .FirstOrDefaultAsync(p => p.PackageName == packageName, cancellationToken)
            ?? throw new InvalidOperationException($"باكيج {packageName} مش موجود في الـ DB");

        var isTicketUsed = await _context.Orders
            .AnyAsync(o => o.TicketNumber == ticketNumber 
                        && o.PackageId == package.PackageId 
                        && o.OrderStatus != Domain.Enums.OrderStatus.Cancelled, cancellationToken);

        if (isTicketUsed)
            throw new InvalidOperationException($"هذه التذكرة مستخدمة بالفعل في خدمة {packageName}.");
    }
}
