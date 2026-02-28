using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Travora.API.Extensions;
using Travora.API.Middleware;
using Travora.Infrastructure.Data;
using Travora.Infrastructure.Data.Seeders;

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

app.MapHub<Travora.API.Hubs.LiveTrackingHub>("/hubs/admin/live-tracking");

app.Run();
