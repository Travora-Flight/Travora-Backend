using Travora.API.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ===== تسجيل الخدمات =====
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger مع JWT
builder.Services.AddSwaggerWithJwt();

// JWT Authentication
builder.Services.AddJwtAuthentication(builder.Configuration);

// CORS
builder.Services.AddCorsPolicy(builder.Configuration);

// خدمات البنية التحتية (DB, Redis, JWT Generator, Cloudinary, Email, HttpClients)
builder.Services.AddInfrastructureServices(builder.Configuration);

// SignalR
builder.Services.AddSignalR();

var app = builder.Build();

// ===== الـ Middleware Pipeline =====
app.UseTravoraMiddleware();

app.Run();
