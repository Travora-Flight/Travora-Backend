using Microsoft.EntityFrameworkCore;
using Travora.Domain.Common;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // ===== المستخدمين =====
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Employee> Employees => Set<Employee>();

    // ===== الطيران =====
    public DbSet<Aircraft> Aircrafts => Set<Aircraft>();
    public DbSet<Airline> Airlines => Set<Airline>();
    public DbSet<Airport> Airports => Set<Airport>();
    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<FlightPositionHistory> FlightPositionHistories => Set<FlightPositionHistory>();
    public DbSet<FlightPrediction> FlightPredictions => Set<FlightPrediction>();
    public DbSet<CodeShareFlight> CodeShareFlights => Set<CodeShareFlight>();
    public DbSet<SavedFlight> SavedFlights => Set<SavedFlight>();
    public DbSet<BoardingPass> BoardingPasses => Set<BoardingPass>();

    // ===== الجغرافيا =====
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Location> Locations => Set<Location>();

    // ===== الطلبات والخدمات =====
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderCompanion> OrderCompanions => Set<OrderCompanion>();
    public DbSet<OrderService> OrderServices => Set<OrderService>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageService> PackageServices => Set<PackageService>();
    public DbSet<Service> Services => Set<Service>();

    // ===== الشنط =====
    public DbSet<Baggage> Baggages => Set<Baggage>();
    public DbSet<BaggagePhoto> BaggagePhotos => Set<BaggagePhoto>();
    public DbSet<BaggageTracking> BaggageTrackings => Set<BaggageTracking>();
    public DbSet<SecurityLock> SecurityLocks => Set<SecurityLock>();
    public DbSet<QrScan> QrScans => Set<QrScan>();

    // ===== المرافقين =====
    public DbSet<Companion> Companions => Set<Companion>();
    public DbSet<CustomerCompanion> CustomerCompanions => Set<CustomerCompanion>();

    // ===== الجمارك =====
    public DbSet<CustomsDeclaration> CustomsDeclarations => Set<CustomsDeclaration>();
    public DbSet<CustomsItem> CustomsItems => Set<CustomsItem>();

    // ===== المستندات =====
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<PassportValidation> PassportValidations => Set<PassportValidation>();

    // ===== المالية =====
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Refund> Refunds => Set<Refund>();

    // ===== التتبع =====
    public DbSet<DriverTracking> DriverTrackings => Set<DriverTracking>();
    public DbSet<Checkpoint> Checkpoints => Set<Checkpoint>();

    // ===== الطقس =====
    public DbSet<WeatherSnapshot> WeatherSnapshots => Set<WeatherSnapshot>();
    public DbSet<CloudLayer> CloudLayers => Set<CloudLayer>();

    // ===== أخرى =====
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<LoginLog> LoginLogs => Set<LoginLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // تطبيق كل الـ Configurations من الـ Assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // فلتر Soft Delete العام لكل Entity بتطبق ISoftDelete
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var property = System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
                var condition = System.Linq.Expressions.Expression.Not(property);
                var filter = System.Linq.Expressions.Expression.Lambda(condition, parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<IHasTimestamps>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
