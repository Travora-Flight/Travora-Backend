using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Travora.API.Extensions;
using Travora.API.Middleware;
using Travora.Infrastructure.Data;
using Travora.Infrastructure.Data.Seeders;
using Hangfire;
using QuestPDF.Infrastructure;
using Travora.Application.Interfaces.Services.Admin;
using Travora.Application.Interfaces.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== Register Services =====
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // This line converts numbers to Enum names in Swagger and the whole API
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();

// Swagger with JWT
builder.Services.AddSwaggerWithJwt();

// JWT Authentication + Authorization Policies
builder.Services.AddJwtAuthentication(builder.Configuration);

// CORS
builder.Services.AddCorsPolicy(builder.Configuration);

// Rate Limiting (Protection against Brute Force and DDoS)
builder.Services.AddRateLimitingPolicies();

// Infrastructure services (DB, Redis, JWT Generator, AuthService, AdminService, Cloudinary, Email)
builder.Services.AddInfrastructureServices(builder.Configuration);

// SignalR
builder.Services.AddSignalR();

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// ===== Seed Data on Startup =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db);
}

// ===== Middleware Pipeline =====
// 1. Exception Handler (First thing - catches any error before it reaches the client)
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// 2. Rate Limiting (Protection against excessive requests)
app.UseRateLimiter();


app.UseWebSockets();

// 3. Rest of Middleware
app.UseTravoraMiddleware();

// 4. Hangfire Dashboard (optional secure config later, for now just open map)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // By default Hangfire allows local requests only which is fine for dev
    DashboardTitle = "Travora Background Jobs"
});

using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobManager.AddOrUpdate<IFlightStatusUpdaterJob>(
        "flight-status-updater",
        job => job.UpdateFlightStatusesAsync(),
        "*/10 * * * *"
    );
    recurringJobManager.AddOrUpdate<IFlightDelayPredictionJob>(
        "flight-delay-predictor",
        job => job.PredictUpcomingFlightDelaysAsync(),
        "*/15 * * * *"
    );
}

app.MapHub<Travora.API.Hubs.LiveTrackingHub>("/hubs/admin/live-tracking");
app.MapHub<Travora.API.Hubs.EmployeeHub>("/hubs/employee");
app.MapHub<Travora.API.Hubs.NotificationHub>("/hubs/notifications");

app.Run();
