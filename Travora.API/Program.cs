using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Travora.API.Extensions;
using Travora.API.Middleware;
using Travora.Infrastructure.Data;
using Travora.Infrastructure.Data.Seeders;
using Hangfire;
using QuestPDF.Infrastructure;
using Travora.Application.Interfaces.Services.Admin;

var builder = WebApplication.CreateBuilder(args);

// ===== تسجيل الخدمات =====
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // السطر ده هو اللي بيحول الأرقام لأسماء الـ Enum في السواجر والـ API كله
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();

// Swagger مع JWT
builder.Services.AddSwaggerWithJwt();

// JWT Authentication + Authorization Policies
builder.Services.AddJwtAuthentication(builder.Configuration);

// CORS
builder.Services.AddCorsPolicy(builder.Configuration);

// Rate Limiting (حماية ضد الـ Brute Force و DDoS)
builder.Services.AddRateLimitingPolicies();

// خدمات البنية التحتية (DB, Redis, JWT Generator, AuthService, AdminService, Cloudinary, Email)
builder.Services.AddInfrastructureServices(builder.Configuration);

// SignalR
builder.Services.AddSignalR();

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// ===== Seed Data عند التشغيل =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db);
}

// ===== الـ Middleware Pipeline =====
// 1. Exception Handler (أول حاجة - يمسك أي Error قبل ما يوصل للعميل)
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// 2. Rate Limiting (حماية ضد الطلبات الكتيرة)
app.UseRateLimiter();

// 3. باقي الـ Middleware
app.UseTravoraMiddleware();

// 4. Hangfire Dashboard (optional secure config later, for now just open map)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // By default Hangfire allows local requests only which is fine for dev
    DashboardTitle = "Travora Background Jobs"
});

app.MapHub<Travora.API.Hubs.LiveTrackingHub>("/hubs/admin/live-tracking");
app.MapHub<Travora.API.Hubs.EmployeeHub>("/hubs/employee");
app.MapHub<Travora.API.Hubs.NotificationHub>("/hubs/notifications");

app.Run();
