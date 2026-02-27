using Microsoft.EntityFrameworkCore;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.Data.Seeders;

public static class DataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        await SeedAdminAsync(db);
        await SeedCheckpointsAsync(db);
        await SeedVehiclesAsync(db);
        await SeedAppSettingsAsync(db);
    }

    private static async Task SeedAppSettingsAsync(ApplicationDbContext db)
    {
        if (await db.AppSettings.AnyAsync()) return;

        db.AppSettings.Add(new AppSettings
        {
            CompanyName = "Travora",
            CompanyEmail = "info@travora.com",
            CompanyPhone = "+20 100 123 4567",
            CompanyAddress = "Cairo, Egypt",
            Timezone = "+2 GMT",
            Language = "English",
            ShowEmployeeNamesOnMap = true,
            AutoRefresh = true,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminAsync(ApplicationDbContext db)
    {
        if (await db.Admins.AnyAsync()) return;

        db.Admins.Add(new Admin
        {
            Email = "admin@travora.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@1234"),
            FullName = "Travora Admin",
            Username = "admin",
            IsSuperAdmin = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedCheckpointsAsync(ApplicationDbContext db)
    {
        if (await db.Checkpoints.AnyAsync()) return;

        var checkpoints = new List<Checkpoint>
        {
            new() { CheckpointName = "Pickup Point",         CheckpointType = CheckpointType.PickupPoint,        SequenceOrder = 1 },
            new() { CheckpointName = "Customs",              CheckpointType = CheckpointType.Customs,             SequenceOrder = 2 },
            new() { CheckpointName = "Security Check",       CheckpointType = CheckpointType.SecurityCheck,       SequenceOrder = 3 },
            new() { CheckpointName = "Airport Terminal",     CheckpointType = CheckpointType.AirportTerminal,     SequenceOrder = 4 },
            new() { CheckpointName = "Airport Gate",         CheckpointType = CheckpointType.AirportGate,         SequenceOrder = 5 },
            new() { CheckpointName = "Airport Baggage Belt", CheckpointType = CheckpointType.AirportBaggageBelt,  SequenceOrder = 6 },
            new() { CheckpointName = "Delivery Point",       CheckpointType = CheckpointType.DeliveryPoint,       SequenceOrder = 7 },
            new() { CheckpointName = "Transit Hub",          CheckpointType = CheckpointType.TransitHub,          SequenceOrder = 8 },
        };

        db.Checkpoints.AddRange(checkpoints);
        await db.SaveChangesAsync();
    }

    private static async Task SeedVehiclesAsync(ApplicationDbContext db)
    {
        if (await db.Vehicles.AnyAsync()) return;

        var vehicles = new List<Vehicle>
        {
            new() { PlateNumber = "ABC-123", Brand = "Toyota", Model = "Hiace",   Year = 2022, Color = "White",  Capacity = 8 },
            new() { PlateNumber = "DEF-456", Brand = "Ford",   Model = "Transit", Year = 2023, Color = "White",  Capacity = 10 },
            new() { PlateNumber = "GHI-789", Brand = "Toyota", Model = "Hiace",   Year = 2021, Color = "Silver", Capacity = 8 },
        };

        db.Vehicles.AddRange(vehicles);
        await db.SaveChangesAsync();
    }
}
