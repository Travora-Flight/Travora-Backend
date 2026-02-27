using Microsoft.EntityFrameworkCore;
using Travora.API.Configurations;
using Travora.Shared.Settings;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.External.Communication;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Infrastructure.Data;
using Travora.Infrastructure.ExternalServices.Communication;
using Travora.Infrastructure.ExternalServices.FileStorage.Cloudinary;
using Travora.Infrastructure.Identity.Services;

namespace Travora.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // قاعدة البيانات
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                sqlOptions.EnableRetryOnFailure(3);
            })
        );

        // Redis
        var redisSettings = configuration.GetSection("RedisSettings").Get<RedisSettings>();
        if (redisSettings != null)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisSettings.ConnectionString;
                options.InstanceName = redisSettings.InstanceName;
            });
        }

        // JWT Token Generator + Auth Service
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
        services.AddSingleton<IJwtTokenGenerator>(new JwtTokenGenerator(jwtSettings));
        services.AddScoped<IAuthService, AuthService>();

        // Admin Services
        services.AddScoped<IAdminAccountService, Travora.Infrastructure.AdminPanel.Services.AdminAccountService>();
        services.AddScoped<IAdminSettingsService, Travora.Infrastructure.AdminPanel.Services.AdminSettingsService>();
        services.AddScoped<IAdminDashboardService, Travora.Infrastructure.AdminPanel.Services.AdminDashboardService>();

        // Cloudinary Settings + Service
        var cloudinarySettings = configuration.GetSection("CloudinarySettings").Get<CloudinarySettings>();
        if (cloudinarySettings != null)
        {
            services.AddSingleton(cloudinarySettings);
            services.AddSingleton<ICloudinaryService>(new CloudinaryService(cloudinarySettings));
        }

        // Email Settings + Service
        var emailSettings = configuration.GetSection("MailSettings").Get<EmailSettings>();
        if (emailSettings != null)
        {
            services.AddSingleton(emailSettings);
            services.AddScoped<IEmailService, EmailService>();
        }

        // App Settings (API integrations)
        services.Configure<AirlineApiSettings>(configuration.GetSection("AirlineApi"));
        services.Configure<AviationEdgeSettings>(configuration.GetSection("AviationEdge"));
        services.Configure<AviationWeatherSettings>(configuration.GetSection("AviationWeather"));
        services.Configure<GeocodingSettings>(configuration.GetSection("Geocoding"));
        services.Configure<PassportOcrSettings>(configuration.GetSection("PassportOcr"));
        services.Configure<SeedSettings>(configuration.GetSection("SeedSettings"));

        // HttpClient للـ APIs الخارجية
        services.AddHttpClient("AirlineApi", client =>
        {
            var airlineApi = configuration.GetSection("AirlineApi").Get<AirlineApiSettings>();
            if (airlineApi != null)
            {
                client.BaseAddress = new Uri(airlineApi.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(airlineApi.TimeoutSeconds);
            }
        });

        services.AddHttpClient("AviationEdge", client =>
        {
            var aviationEdge = configuration.GetSection("AviationEdge").Get<AviationEdgeSettings>();
            if (aviationEdge != null)
            {
                client.BaseAddress = new Uri(aviationEdge.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(aviationEdge.TimeoutSeconds);
            }
        });

        return services;
    }
}
