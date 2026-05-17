using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Pricing;
using Travora.Application.Interfaces;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminPricingService : IAdminPricingService
{
    private readonly ApplicationDbContext _db;

    public AdminPricingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PricingOverviewResponse> GetPricingOverviewAsync()
    {
        var activeServicesCount = await _db.Services.CountAsync(s => s.IsActive);
        var totalServicesCount = await _db.Services.CountAsync();
        var totalPackagesCount = await _db.Packages.CountAsync();

        var servicesList = await _db.Services
            .Select(s => new ServiceDetailResponse
            {
                ServiceId = s.ServiceId,
                Code = s.ServiceCode,
                Name = s.ServiceName,
                Type = s.ServiceType.ToString().ToLower(),
                Description = s.Description,
                BasePrice = s.BasePrice,
                Currency = "EGP",
                IsActive = s.IsActive
            }).ToListAsync();

        var packagesList = await _db.Packages
            .Include(p => p.PackageServices)
                .ThenInclude(ps => ps.Service)
            .Select(p => new PackageDetailResponse
            {
                PackageId = p.PackageId,
                Code = p.PackageCode,
                PackageName = p.PackageName,
                IsActive = p.IsActive,
                TotalPrice = p.TotalBasePrice,
                FinalPrice = p.TotalBasePrice - (p.TotalBasePrice * (p.Discount ?? 0) / 100),
                Discount = p.Discount,
                Currency = p.Currency,
                IncludedCompanions = p.IncludedCompanionsCount,
                ExtraCompanionPrice = p.ExtraCompanionPrice,
                IncludedBags = p.IncludedBaggageCount,
                ExtraBagPrice = p.ExtraBaggagePrice,
                ServicesIncluded = p.PackageServices.Select(ps => new PackageServiceDetail
                {
                    Name = ps.Service.ServiceName,
                    Phase = ps.ExecutionPhase.ToString().ToLower(),
                    IsFree = ps.IncludedInBase
                }).ToList()
            }).ToListAsync();

        return new PricingOverviewResponse
        {
            Stats = new PricingStatsResponse
            {
                TotalServices = totalServicesCount,
                TotalPackages = totalPackagesCount,
                ActiveServices = activeServicesCount
            },
            Services = servicesList,
            Packages = packagesList
        };
    }

    public async Task<List<ServiceDetailResponse>> GetServicesAsync(Travora.Domain.Enums.ActivationFilterStatus status)
    {
        var query = _db.Services.AsQueryable();

        if (status == Travora.Domain.Enums.ActivationFilterStatus.Active)
            query = query.Where(s => s.IsActive);
        else if (status == Travora.Domain.Enums.ActivationFilterStatus.Inactive)
            query = query.Where(s => !s.IsActive);

        return await query.Select(s => new ServiceDetailResponse
        {
            ServiceId = s.ServiceId,
            Code = s.ServiceCode,
            Name = s.ServiceName,
            Type = s.ServiceType.ToString().ToLower(),
            Description = s.Description,
            BasePrice = s.BasePrice,
            Currency = "EGP",
            IsActive = s.IsActive
        }).ToListAsync();
    }

    public async Task<List<PackageDetailResponse>> GetPackagesAsync(Travora.Domain.Enums.ActivationFilterStatus status)
    {
        var query = _db.Packages.AsQueryable();

        if (status == Travora.Domain.Enums.ActivationFilterStatus.Active)
            query = query.Where(p => p.IsActive);
        else if (status == Travora.Domain.Enums.ActivationFilterStatus.Inactive)
            query = query.Where(p => !p.IsActive);

        return await query
            .Include(p => p.PackageServices)
                .ThenInclude(ps => ps.Service)
            .Select(p => new PackageDetailResponse
            {
                PackageId = p.PackageId,
                Code = p.PackageCode,
                PackageName = p.PackageName,
                IsActive = p.IsActive,
                TotalPrice = p.TotalBasePrice,
                FinalPrice = p.TotalBasePrice - (p.TotalBasePrice * (p.Discount ?? 0) / 100),
                Discount = p.Discount,
                Currency = p.Currency,
                IncludedCompanions = p.IncludedCompanionsCount,
                ExtraCompanionPrice = p.ExtraCompanionPrice,
                IncludedBags = p.IncludedBaggageCount,
                ExtraBagPrice = p.ExtraBaggagePrice,
                ServicesIncluded = p.PackageServices.Select(ps => new PackageServiceDetail
                {
                    Name = ps.Service.ServiceName,
                    Phase = ps.ExecutionPhase.ToString().ToLower(),
                    IsFree = ps.IncludedInBase
                }).ToList()
            }).ToListAsync();
    }



    // --- Services CRUD ---

    public async Task<object> CreateServiceAsync(CreateServiceRequest request)
    {
        var service = new Travora.Domain.Entities.Service
        {
            ServiceName = request.ServiceName,
            BasePrice = request.BasePrice,
            ServiceType = MapServiceType(request.Type),
            Description = request.Description,
            IsActive = request.IsActive
        };

        _db.Services.Add(service);
        await _db.SaveChangesAsync(); // To get ServiceId

        service.ServiceCode = $"SRV{service.ServiceId:D3}";
        await _db.SaveChangesAsync();

        return new { success = true, serviceId = service.ServiceId, code = service.ServiceCode };
    }

    public async Task<bool> UpdateServiceAsync(int serviceId, UpdateServiceRequest request)
    {
        var service = await _db.Services.FindAsync(serviceId)
            ?? throw new KeyNotFoundException("Service not found");

        if (request.ServiceName != null) service.ServiceName = request.ServiceName;
        if (request.BasePrice.HasValue) service.BasePrice = request.BasePrice.Value;
        if (request.Description != null) service.Description = request.Description;
        if (request.Type != null) service.ServiceType = MapServiceType(request.Type);

        service.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateServiceStatusAsync(int serviceId, UpdateStatusRequest request)
    {
        var service = await _db.Services.FindAsync(serviceId)
            ?? throw new KeyNotFoundException("Service not found");

        if (request.IsActive == false)
        {
            // Check if this service is used in any active packages
            var activeLinkedPackage = await _db.Packages
                .Where(p => p.IsActive && p.PackageServices.Any(ps => ps.ServiceId == serviceId))
                .FirstOrDefaultAsync();

            if (activeLinkedPackage != null)
            {
                throw new InvalidOperationException($"Cannot deactivate service '{service.ServiceName}' because it is currently used in the active package '{activeLinkedPackage.PackageName}'. Please deactivate or update the package first.");
            }
        }

        service.IsActive = request.IsActive;

        if (service.IsActive == false)
        {
            var affectedPackages = await _db.Packages
                .Include(p => p.PackageServices)
                    .ThenInclude(ps => ps.Service)
                .Where(p => p.PackageServices.Any(ps => ps.ServiceId == serviceId))
                .ToListAsync();

            foreach (var pkg in affectedPackages)
            {
                if (pkg.PackageServices.All(ps => !ps.Service.IsActive))
                {
                    pkg.IsActive = false;
                }
            }
        }

        service.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteServiceAsync(int serviceId)
    {
        var service = await _db.Services.FindAsync(serviceId)
            ?? throw new KeyNotFoundException("Service not found");

        // Check if this service is used in any packages (active or inactive)
        var linkedPackage = await _db.Packages
            .Where(p => p.PackageServices.Any(ps => ps.ServiceId == serviceId))
            .FirstOrDefaultAsync();

        if (linkedPackage != null)
        {
            throw new InvalidOperationException($"Cannot delete service '{service.ServiceName}' because it is currently used in package '{linkedPackage.PackageName}'. Please remove it from the package first.");
        }

        service.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    // --- Packages CRUD ---

    public async Task<object> CreatePackageAsync(CreatePackageRequest request)
    {
        decimal totalPrice = 0;
        var serviceDetails = new List<PackageServiceDetail>();

        foreach(var s in request.Services)
        {
            var srv = await _db.Services.FindAsync(s.ServiceId);
            if (srv != null)
            {
                if (!s.IsFree)
                {
                    totalPrice += srv.BasePrice;
                }
                serviceDetails.Add(new PackageServiceDetail 
                {
                    Name = srv.ServiceName,
                    Phase = s.Phase,
                    IsFree = s.IsFree
                });
            }
        }

        var package = new Travora.Domain.Entities.Package
        {
            PackageName = request.PackageName,
            TotalBasePrice = totalPrice,
            Discount = request.Discount ?? 0,
            IsActive = request.IsActive,
            IncludedCompanionsCount = request.IncludedCompanions,
            ExtraCompanionPrice = request.ExtraCompanionPrice,
            MaxCompanionsLimit = request.MaxCompanionsLimit,
            IncludedBaggageCount = request.IncludedBags,
            ExtraBaggagePrice = request.ExtraBagPrice,
            MaxBaggageLimit = request.MaxBagsLimit,
            Description = request.Description
        };

        foreach (var s in request.Services)
        {
            package.PackageServices.Add(new Travora.Domain.Entities.PackageService
            {
                ServiceId = s.ServiceId,
                ExecutionPhase = MapExecutionPhase(s.Phase),
                IncludedInBase = s.IsFree
            });
        }

        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        package.PackageCode = $"PKG{package.PackageId:D3}";
        await _db.SaveChangesAsync();

        return new CreatePackageResponse
        {
            PackageId = package.PackageId,
            Code = package.PackageCode,
            PackageName = package.PackageName,
            TotalPrice = package.TotalBasePrice,
            FinalPrice = package.TotalBasePrice - (package.TotalBasePrice * (package.Discount ?? 0) / 100),
            Discount = package.Discount,
            Services = serviceDetails
        };
    }

    public async Task<bool> UpdatePackageAsync(int packageId, UpdatePackageRequest request)
    {
        var package = await _db.Packages
            .Include(p => p.PackageServices)
            .FirstOrDefaultAsync(p => p.PackageId == packageId)
            ?? throw new KeyNotFoundException("Package not found");

        if (request.PackageName != null) package.PackageName = request.PackageName;
        if (request.Discount != null) package.Discount = request.Discount; // Can be null, but request.Discount is decimal?

        if (request.IncludedCompanions.HasValue) package.IncludedCompanionsCount = request.IncludedCompanions.Value;
        if (request.ExtraCompanionPrice.HasValue) package.ExtraCompanionPrice = request.ExtraCompanionPrice.Value;
        if (request.MaxCompanionsLimit != null) package.MaxCompanionsLimit = request.MaxCompanionsLimit;

        if (request.IncludedBags.HasValue) package.IncludedBaggageCount = request.IncludedBags.Value;
        if (request.ExtraBagPrice.HasValue) package.ExtraBaggagePrice = request.ExtraBagPrice.Value;
        if (request.MaxBagsLimit != null) package.MaxBaggageLimit = request.MaxBagsLimit;

        if (request.Description != null) package.Description = request.Description;

        if (request.Services != null)
        {
            // Fully replace the services mapping
            _db.RemoveRange(package.PackageServices);
            decimal newTotalPrice = 0;

            foreach (var s in request.Services)
            {
                var srv = await _db.Services.FindAsync(s.ServiceId);
                if (srv != null && !s.IsFree)
                {
                    newTotalPrice += srv.BasePrice;
                }

                package.PackageServices.Add(new Travora.Domain.Entities.PackageService
                {
                    ServiceId = s.ServiceId,
                    ExecutionPhase = MapExecutionPhase(s.Phase),
                    IncludedInBase = s.IsFree
                });
            }
            package.TotalBasePrice = newTotalPrice;
        }

        package.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdatePackageStatusAsync(int packageId, UpdateStatusRequest request)
    {
        var package = await _db.Packages.FindAsync(packageId)
            ?? throw new KeyNotFoundException("Package not found");

        package.IsActive = request.IsActive;
        package.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeletePackageAsync(int packageId)
    {
        var package = await _db.Packages.FindAsync(packageId)
            ?? throw new KeyNotFoundException("Package not found");

        package.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    // --- Helpers ---
    private Travora.Domain.Enums.ServiceType MapServiceType(string type) => type.ToLower() switch
    {
        "pickup" => Travora.Domain.Enums.ServiceType.Pickup,
        "delivery" => Travora.Domain.Enums.ServiceType.Delivery,
        "tracking" => Travora.Domain.Enums.ServiceType.Tracking,
        _ => throw new ArgumentException("Invalid service type")
    };

    private Travora.Domain.Enums.ExecutionPhase MapExecutionPhase(string phase) => phase.ToLower() switch
    {
        "pickup" => Travora.Domain.Enums.ExecutionPhase.Pickup,
        "airport" => Travora.Domain.Enums.ExecutionPhase.AirportCheckin,
        "delivery" => Travora.Domain.Enums.ExecutionPhase.Delivery,
        _ => throw new ArgumentException("Invalid phase")
    };
}
