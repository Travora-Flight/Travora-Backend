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
        var services = await _db.Services
            .Where(s => s.IsActive)
            .Select(s => new ServicePricingItem
            {
                ServiceId = s.ServiceId,
                ServiceName = s.ServiceName,
                BasePrice = s.BasePrice
            }).ToListAsync();

        var packages = await _db.Packages
            .Where(p => p.IsActive)
            .Select(p => new PackagePricingItem
            {
                PackageId = p.PackageId,
                PackageName = p.PackageName,
                TotalBasePrice = p.TotalBasePrice,
                ExtraBaggagePrice = p.ExtraBaggagePrice,
                ExtraCompanionPrice = p.ExtraCompanionPrice
            }).ToListAsync();

        return new PricingOverviewResponse
        {
            Services = services,
            Packages = packages
        };
    }

    public async Task<bool> UpdateServicePriceAsync(int serviceId, UpdateServicePriceRequest request)
    {
        var service = await _db.Services.FindAsync(serviceId)
            ?? throw new KeyNotFoundException("Service not found");

        service.BasePrice = request.NewBasePrice;
        service.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdatePackagePricingAsync(int packageId, UpdatePackagePricingRequest request)
    {
        var package = await _db.Packages.FindAsync(packageId)
            ?? throw new KeyNotFoundException("Package not found");

        package.ExtraBaggagePrice = request.NewExtraBaggagePrice;
        package.ExtraCompanionPrice = request.NewExtraCompanionPrice;
        package.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }
}
