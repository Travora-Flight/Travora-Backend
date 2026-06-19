using Microsoft.EntityFrameworkCore;
using Travora.Domain.Common;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // ===== Users =====
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Employee> Employees => Set<Employee>();

    // ===== Aviation =====
    public DbSet<Aircraft> Aircrafts => Set<Aircraft>();
    public DbSet<Airline> Airlines => Set<Airline>();
    public DbSet<Airport> Airports => Set<Airport>();
    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<FlightPrediction> FlightPredictions => Set<FlightPrediction>();
    public DbSet<SavedFlight> SavedFlights => Set<SavedFlight>();
    public DbSet<BoardingPass> BoardingPasses => Set<BoardingPass>();

    // ===== Geography =====
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Location> Locations => Set<Location>();

    // ===== Orders and Services =====
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderCompanion> OrderCompanions => Set<OrderCompanion>();
    public DbSet<OrderService> OrderServices => Set<OrderService>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageService> PackageServices => Set<PackageService>();
    public DbSet<Service> Services => Set<Service>();

    // ===== Baggage =====
    public DbSet<Baggage> Baggages => Set<Baggage>();
    public DbSet<BaggagePhoto> BaggagePhotos => Set<BaggagePhoto>();
    public DbSet<BaggageTracking> BaggageTrackings => Set<BaggageTracking>();
    public DbSet<SecurityLock> SecurityLocks => Set<SecurityLock>();
    public DbSet<QrScan> QrScans => Set<QrScan>();

    // ===== Companions =====
    public DbSet<Companion> Companions => Set<Companion>();

    // ===== Customs =====
    public DbSet<CustomsDeclaration> CustomsDeclarations => Set<CustomsDeclaration>();
    public DbSet<CustomsItem> CustomsItems => Set<CustomsItem>();
    public DbSet<CustomsItemInvoice> CustomsItemInvoices => Set<CustomsItemInvoice>();

    // ===== Documents =====
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<PassportValidation> PassportValidations => Set<PassportValidation>();

    // ===== Finance =====
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Refund> Refunds => Set<Refund>();

    // ===== Tracking =====
    public DbSet<DriverTracking> DriverTrackings => Set<DriverTracking>();
    public DbSet<Checkpoint> Checkpoints => Set<Checkpoint>();

    // ===== Weather =====
    public DbSet<WeatherSnapshot> WeatherSnapshots => Set<WeatherSnapshot>();

    // ===== Others =====
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

        // Apply all Configurations from Assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // ===== Decimal Precision =====
        modelBuilder.Entity<Baggage>()
            .Property(b => b.TotalWeight)
            .HasPrecision(18, 2);

        // Global Soft Delete filter for every Entity that implements ISoftDelete
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
