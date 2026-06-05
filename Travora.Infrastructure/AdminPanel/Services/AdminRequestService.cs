using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Requests;
using Travora.Application.Interfaces;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminRequestService : IAdminRequestService
{
    private readonly ApplicationDbContext _db;

    public AdminRequestService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<RequestPagedResponse> GetRequestsAsync(string? search, string? filter, string? status, int page, int pageSize)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderServices)
                .ThenInclude(os => os.AssignedEmployee)
            .AsQueryable();

        // 1. Search
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(o => 
                (o.Customer != null && (o.Customer.Firstname.ToLower().Contains(searchLower) || o.Customer.Lastname.ToLower().Contains(searchLower))) ||
                o.OrderId.ToString().Contains(searchLower));
        }

        // 2. Filter (Time)
        var now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            if (filter.Equals("today", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(o => o.CreatedAt.Date == now.Date);
            }
            else if (filter.Equals("this_week", StringComparison.OrdinalIgnoreCase))
            {
                var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
                query = query.Where(o => o.CreatedAt.Date >= startOfWeek);
            }
        }

        // 3. Status
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(o => o.OrderStatus == parsedStatus);
            }
        }

        var total = await query.CountAsync();

        var requests = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new RequestListResponse
            {
                OrderId = o.OrderId,
                ClientName = o.Customer != null ? $"{o.Customer.Firstname} {o.Customer.Lastname}" : "Unknown",
                Type = o.PackageId > 0 ? "package_service" : "service",
                Status = o.OrderStatus.ToString().ToLower(),
                AssignedEmployee = o.OrderServices.FirstOrDefault(os => os.AssignedEmployee != null) != null
                    ? (o.OrderServices.FirstOrDefault(os => os.AssignedEmployee != null)!.AssignedEmployee!.Firstname + " " + o.OrderServices.FirstOrDefault(os => os.AssignedEmployee != null)!.AssignedEmployee!.Lastname)
                    : "Not Assigned",
                Time = o.CreatedAt.ToString("hh:mm tt")
            })
            .ToListAsync();

        return new RequestPagedResponse
        {
            Requests = requests,
            Total = total
        };
    }

    public async Task<RequestDetailResponse> GetRequestDetailsAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Package)
            .Include(o => o.PickupLocation)
            .Include(o => o.DeliveryLocation)
            .Include(o => o.CustomsDeclarations)
                .ThenInclude(cd => cd.CustomsItems)
                    .ThenInclude(ci => ci.Invoices)
            .Include(o => o.OrderServices)
                .ThenInclude(os => os.AssignedEmployee)
            .Include(o => o.OrderServices)
                .ThenInclude(os => os.PackageService)
                    .ThenInclude(ps => ps.Service)
            .FirstOrDefaultAsync(o => o.OrderId == orderId)
            ?? throw new KeyNotFoundException("Order not found");

        var assignedEmp = order.OrderServices.FirstOrDefault(os => os.AssignedEmployee != null)?.AssignedEmployee;
        
        // Real timeline based on actual timestamps
        var timeline = new List<TimelineItem>();
        
        // 1. Request Sent
        timeline.Add(new TimelineItem 
        { 
            Event = "Request Sent", 
            Time = order.CreatedAt.ToString("hh:mm tt"), 
            IsDone = true 
        });
        
        // 2. Assign Employee
        var assignedService = order.OrderServices
            .Where(os => os.AssignedAt.HasValue)
            .OrderBy(os => os.AssignedAt)
            .FirstOrDefault();
        var assignTime = assignedService?.AssignedAt;
        
        timeline.Add(new TimelineItem
        {
            Event = "Assign Employee",
            Time = assignTime?.ToString("hh:mm tt"),
            IsDone = assignTime.HasValue
        });

        // 3. Begin to Execute
        var startedService = order.OrderServices
            .Where(os => os.ActualStartTime.HasValue)
            .OrderBy(os => os.ActualStartTime)
            .FirstOrDefault();
        var executeTime = startedService?.ActualStartTime;

        timeline.Add(new TimelineItem
        {
            Event = "Begin to Execute",
            Time = executeTime?.ToString("hh:mm tt"),
            IsDone = executeTime.HasValue
        });

        // 4. Request Done
        var lastCompletedService = order.OrderServices
            .Where(os => os.ActualEndTime.HasValue)
            .OrderByDescending(os => os.ActualEndTime)
            .FirstOrDefault();
        var doneTime = lastCompletedService?.ActualEndTime ?? (order.OrderStatus == OrderStatus.Completed ? order.UpdatedAt : null);

        timeline.Add(new TimelineItem
        {
            Event = "Request Done",
            Time = (order.OrderStatus == OrderStatus.Completed && doneTime.HasValue) ? doneTime.Value.ToString("hh:mm tt") : null,
            IsDone = order.OrderStatus == OrderStatus.Completed
        });

        // Customs details logic
        AdminCustomsDetailsDto? customsDetails = null;
        var packageName = order.Package?.PackageName;

        if (packageName == Travora.Domain.Constants.PackageNames.DoorToDoor)
        {
            var declaration = order.CustomsDeclarations.FirstOrDefault();
            if (declaration != null && declaration.CustomsType == Domain.Enums.CustomsType.RedField)
            {
                customsDetails = new AdminCustomsDetailsDto
                {
                    HasCustoms = true,
                    CustomsType = "RedField",
                    TotalDeclaredValue = declaration.TotalDeclaredValue,
                    TotalCustomsFee = declaration.TotalCustomsFee,
                    Notes = declaration.Notes,
                    Items = declaration.CustomsItems.Select(ci => new AdminCustomsItemDto
                    {
                        ItemDescription = ci.ItemDescription,
                        Category = ci.ExternalCategoryName ?? string.Empty,
                        Quantity = ci.Quantity,
                        DeclaredValue = ci.DeclaredValue,
                        TotalValue = ci.TotalValue,
                        CustomsRatePercentage = ci.CustomsRatePercentage,
                        CustomsFee = ci.TotalCustomsValue,
                        InvoiceUrls = ci.Invoices.Select(inv => inv.InvoicePath).ToList()
                    }).ToList()
                };
            }
            else
            {
                customsDetails = new AdminCustomsDetailsDto
                {
                    HasCustoms = false,
                    CustomsType = "GreenField",
                    CustomsMessage = "Green line selected, no customs fees apply"
                };
            }
        }
        else
        {
            customsDetails = new AdminCustomsDetailsDto
            {
                HasCustoms = false,
                CustomsMessage = "No customs for this service"
            };
        }

        return new RequestDetailResponse
        {
            OrderId = order.OrderId,
            Status = order.OrderStatus.ToString().ToLower(),
            ClientInfo = new ClientInfo
            {
                Name = order.Customer != null ? $"{order.Customer.Firstname} {order.Customer.Lastname}" : "Unknown",
                Mobile = order.Customer?.PhoneNumber ?? string.Empty,
                Address = order.PickupLocation?.StreetAddress ?? string.Empty,
                MapUrl = $"https://maps.google.com/?q={order.PickupLocation?.GpsLatitude},{order.PickupLocation?.GpsLongitude}"
            },
            ServiceDetails = new ServiceDetails
            {
                PackageType = order.Package?.PackageName ?? (order.OrderServices.FirstOrDefault()?.PackageService?.Service?.ServiceName ?? "Unknown Service"),
                AssignedEmployee = assignedEmp != null ? $"{assignedEmp.Firstname} {assignedEmp.Lastname}" : "Not Assigned"
            },
            Timeline = timeline,
            CustomsDetails = customsDetails
        };
    }

    public async Task<bool> AssignEmployeeAsync(int orderId, AssignEmployeeRequest request)
    {
        var order = await _db.Orders
            .Include(o => o.OrderServices)
            .FirstOrDefaultAsync(o => o.OrderId == orderId)
            ?? throw new KeyNotFoundException("Order not found");

        var employee = await _db.Employees.FindAsync(request.EmployeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        // If OrderServiceId is specified -> assign to a specific service
        if (request.OrderServiceId.HasValue)
        {
            var targetService = order.OrderServices
                .FirstOrDefault(s => s.OrderServiceId == request.OrderServiceId.Value)
                ?? throw new KeyNotFoundException("Order service not found");

            if (targetService.ServiceStatus != ServiceStatus.Pending)
                throw new InvalidOperationException("This service is not in Pending status");

            targetService.AssignedEmployeeId = request.EmployeeId;
            targetService.ServiceStatus = ServiceStatus.Assigned;
            targetService.AssignedAt = DateTime.UtcNow;
            targetService.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Fallback: assign to the first Pending service
            var pendingService = order.OrderServices
                .FirstOrDefault(s => s.ServiceStatus == ServiceStatus.Pending)
                ?? throw new InvalidOperationException("No services in Pending status found");

            pendingService.AssignedEmployeeId = request.EmployeeId;
            pendingService.ServiceStatus = ServiceStatus.Assigned;
            pendingService.AssignedAt = DateTime.UtcNow;
            pendingService.UpdatedAt = DateTime.UtcNow;
        }

        // Notification
        _db.Notifications.Add(new Domain.Entities.Notification
        {
            UserId = request.EmployeeId,
            UserType = UserType.Employee,
            NotificationType = NotificationType.OrderUpdated,
            Title = "You have been assigned to a new task",
            Message = "You have been manually assigned by the administration",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = order.OrderId
        });

        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<UnassignedServiceDto>> GetUnassignedServicesAsync()
    {
        var services = await _db.OrderServices
            .Include(os => os.Order).ThenInclude(o => o.Customer)
            .Include(os => os.Order).ThenInclude(o => o.Package)
            .Include(os => os.Order).ThenInclude(o => o.PickupLocation)
            .Include(os => os.PackageService).ThenInclude(ps => ps.Service)
            .Where(os => os.AssignedEmployeeId == null 
                         && os.ServiceStatus == ServiceStatus.Pending
                         && os.Order.Package.PackageCode != Travora.Domain.Constants.PackageCodes.TrackingBaggage)
            .ToListAsync();

        return services.Select(os => new UnassignedServiceDto
        {
            OrderServiceId = os.OrderServiceId,
            OrderId = os.OrderId,
            PackageName = os.Order.Package?.PackageName ?? "Unknown Package",
            ServiceName = os.PackageService?.Service?.ServiceName ?? "Unknown Service",
            ExecutionPhase = os.PackageService?.ExecutionPhase.ToString() ?? string.Empty,
            ScheduledStartTime = os.ScheduledStartTime,
            ScheduledEndTime = os.ScheduledEndTime,
            CustomerName = os.Order.Customer != null ? $"{os.Order.Customer.Firstname} {os.Order.Customer.Lastname}" : "Unknown",
            City = os.Order.PickupLocation?.City ?? string.Empty
        }).ToList();
    }

    public async Task<IEnumerable<AvailableEmployeeDto>> GetAvailableEmployeesForServiceAsync(int orderServiceId)
    {
        var os = await _db.OrderServices
            .Include(s => s.PackageService).ThenInclude(ps => ps.Service)
            .FirstOrDefaultAsync(s => s.OrderServiceId == orderServiceId)
            ?? throw new KeyNotFoundException("Order service not found");

        var phase = os.PackageService?.ExecutionPhase;
        var needsDriver = phase is ExecutionPhase.Pickup or ExecutionPhase.Delivery;
        var needsHandler = phase is ExecutionPhase.DepartureCheckin or ExecutionPhase.ArrivalCheckin;

        if (needsHandler)
        {
            var handlers = await _db.Employees
                .Where(e => e.JobRole == JobRole.BaggageHandler && e.IsActive && !e.IsDeleted)
                .Include(e => e.AssignedOrderServices)
                .ToListAsync();

            return handlers.Select(h => new AvailableEmployeeDto
            {
                EmployeeId = h.EmployeeId,
                Name = $"{h.Firstname} {h.Lastname}",
                Role = h.JobRole.ToString(),
                Shift = h.ShiftType.ToString(),
                VehicleDetails = null
            }).ToList();
        }
        else if (needsDriver)
        {
            var slotStart = os.ScheduledStartTime.TimeOfDay;
            var slotEnd = os.ScheduledEndTime.TimeOfDay;
            var date = os.ScheduledStartTime.Date;

            var drivers = await _db.Employees
                .Include(e => e.Vehicle)
                .Include(e => e.AssignedOrderServices)
                .Where(e => e.JobRole == JobRole.Driver && e.IsActive && !e.IsDeleted && e.VehicleId != null)
                .ToListAsync();

            var availableDrivers = drivers.Where(d =>
                IsShiftCovering(d.ShiftType, slotStart, slotEnd) &&
                !HasConflict(d, date, slotStart, slotEnd));

            return availableDrivers.Select(d => new AvailableEmployeeDto
            {
                EmployeeId = d.EmployeeId,
                Name = $"{d.Firstname} {d.Lastname}",
                Role = d.JobRole.ToString(),
                Shift = d.ShiftType.ToString(),
                VehicleDetails = d.Vehicle != null ? $"{d.Vehicle.Brand} {d.Vehicle.Model} ({d.Vehicle.PlateNumber})" : null
            }).ToList();
        }

        return new List<AvailableEmployeeDto>();
    }

    private bool IsShiftCovering(ShiftType shift, TimeSpan slotStart, TimeSpan slotEnd)
    {
        return shift switch
        {
            ShiftType.Morning => slotStart >= TimeSpan.FromHours(8) && slotEnd <= TimeSpan.FromHours(16),
            ShiftType.Evening => slotStart >= TimeSpan.FromHours(16) && slotEnd <= TimeSpan.FromHours(24),
            ShiftType.Night => (slotStart >= TimeSpan.FromHours(22) || slotStart < TimeSpan.FromHours(8)), 
            ShiftType.rotating => true,
            _ => false
        };
    }

    private bool HasConflict(Employee driver, DateTime date, TimeSpan slotStart, TimeSpan slotEnd)
    {
        return driver.AssignedOrderServices.Any(os =>
            os.ScheduledStartTime.Date == date &&
            os.ScheduledStartTime.TimeOfDay < slotEnd &&
            os.ScheduledEndTime.TimeOfDay > slotStart &&
            os.ServiceStatus != ServiceStatus.Completed);
    }
}
