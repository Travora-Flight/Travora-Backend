using Microsoft.EntityFrameworkCore;
using Travora.Infrastructure.Configurations;
using Travora.API.Configurations;
using Travora.Shared.Settings;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.External.Communication;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Infrastructure.Data;
using Travora.Infrastructure.ExternalServices.Communication;
using Travora.Infrastructure.ExternalServices.FileStorage.Cloudinary;
using Travora.Infrastructure.Identity.Services;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Extensions;

using Travora.Application.Interfaces.Services.Employee;
using Travora.Application.Interfaces.Services.Admin;

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
            .ConfigureWarnings(warnings =>
            {
                // الـ Warnings دي متوقعة بسبب الـ ISoftDelete Query Filter - مش مشكلة
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning);
                // الـ Decimal Precision warnings
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ShadowForeignKeyPropertyCreated);
                // الـ Pending Migration warning - بنعمل migration يدوي
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
            })
        );

        // Upstash Redis REST API
        services.AddHttpClient("UpstashRedis", client =>
        {
            client.BaseAddress = new Uri(configuration["UpstashRedis:RestUrl"]!);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", configuration["UpstashRedis:Token"]);
        });
        services.AddSingleton<IUpstashRedisService, Travora.Infrastructure.ExternalServices.UpstashRedisService>();

        // JWT Token Generator + Auth Service
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
        services.AddSingleton<IJwtTokenGenerator>(new JwtTokenGenerator(jwtSettings));
        services.AddScoped<IAuthService, AuthService>();

        // Admin Services
        services.AddScoped<IAviationSeederService, Travora.Infrastructure.AdminPanel.Services.AviationSeederService>();
        services.AddScoped<IAdminAccountService, Travora.Infrastructure.AdminPanel.Services.AdminAccountService>();
        services.AddScoped<IAdminSettingsService, Travora.Infrastructure.AdminPanel.Services.AdminSettingsService>();
        services.AddScoped<IAdminDashboardService, Travora.Infrastructure.AdminPanel.Services.AdminDashboardService>();
        services.AddScoped<IAdminEmployeeService, Travora.Infrastructure.AdminPanel.Services.AdminEmployeeService>();
        services.AddScoped<IAdminRequestService, Travora.Infrastructure.AdminPanel.Services.AdminRequestService>();
        services.AddScoped<IAdminLiveTrackerService, Travora.Infrastructure.AdminPanel.Services.AdminLiveTrackerService>();
        services.AddScoped<IAdminPassportService, Travora.Infrastructure.AdminPanel.Services.AdminPassportService>();
        services.AddScoped<IAdminPricingService, Travora.Infrastructure.AdminPanel.Services.AdminPricingService>();
        services.AddScoped<IAdminReportService, Travora.Infrastructure.AdminPanel.Services.AdminReportService>();
        services.AddScoped<IAdminVehicleService, Travora.Infrastructure.AdminPanel.Services.AdminVehicleService>();
        services.AddScoped<IAdminCheckpointService, Travora.Infrastructure.AdminPanel.Services.AdminCheckpointService>();
        services.AddScoped<IReportGeneratorJob, Travora.Infrastructure.BackgroundJobs.ReportGeneratorJob>();

        // Employee Services
        services.AddScoped<IEmployeeDashboardService, Travora.Infrastructure.EmployeePanel.Services.EmployeeDashboardService>();
        services.AddScoped<IEmployeeTaskService, Travora.Infrastructure.EmployeePanel.Services.EmployeeTaskService>();
        services.AddScoped<IEmployeeBaggageService, Travora.Infrastructure.EmployeePanel.Services.EmployeeBaggageService>();
        services.AddScoped<IEmployeeLocationService, Travora.Infrastructure.EmployeePanel.Services.EmployeeLocationService>();
        services.AddScoped<IEmployeeNotificationService, Travora.Infrastructure.EmployeePanel.Services.EmployeeNotificationService>();
        services.AddScoped<IEmployeeAccountService, Travora.Infrastructure.EmployeePanel.Services.EmployeeAccountService>();

        // Customer Services
        services.AddScoped<Travora.Application.Interfaces.Services.Customer.ICustomerProfileService, Travora.Infrastructure.CustomerPanel.Services.CustomerProfileService>();
        services.AddScoped<Travora.Application.Interfaces.Services.Customer.ICustomerAuthService, Travora.Infrastructure.CustomerPanel.Services.CustomerAuthService>();
        services.AddScoped<Travora.Application.Interfaces.Services.Customer.IPassportOcrService, Travora.Infrastructure.CustomerPanel.Services.PassportOcrService>();
        services.AddScoped<Travora.Application.Interfaces.Services.Customer.IDoorToDoorOrderService, Travora.Infrastructure.CustomerPanel.Services.DoorToDoorOrderService>();
        services.AddScoped<Travora.Application.Interfaces.Services.Customer.ICarServiceOrderService, Travora.Infrastructure.CustomerPanel.Services.CarServiceOrderService>();
        services.AddScoped<Travora.Application.Interfaces.Services.Customer.IBagTrackingOrderService, Travora.Infrastructure.CustomerPanel.Services.BagTrackingOrderService>();
        services.AddScoped<Travora.Application.Interfaces.Services.Customer.ICustomerOrderService, Travora.Infrastructure.CustomerPanel.Services.CustomerOrderService>();
        services.AddScoped<Travora.Application.Interfaces.Services.Customer.ICustomerNotificationService, Travora.Infrastructure.CustomerPanel.Services.CustomerNotificationService>();

        // Hub Services
        services.AddScoped<Travora.Application.Interfaces.Hubs.ILiveTrackingHubService, Travora.API.Services.LiveTrackingHubService>();
        services.AddScoped<INotificationPusher, Travora.API.Services.NotificationPusher>();

        // Payment Services
        services.AddScoped<IPaymobService, Travora.Infrastructure.Services.PaymobService>();
        services.AddScoped<IRefundService, Travora.Infrastructure.Services.RefundService>();
        services.AddScoped<IPaymentMethodService, Travora.Infrastructure.Services.PaymentMethodService>();

        // Admin Validators
        var fluentValidationAssemblies = new[] { typeof(Travora.Application.Validators.Admin.Employees.CreateEmployeeValidator).Assembly };
        services.AddValidatorsFromAssemblies(fluentValidationAssemblies);
        services.AddFluentValidationAutoValidation();

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
        services.Configure<Travora.Infrastructure.Configurations.GeocodingSettings>(configuration.GetSection("Geocoding"));
        services.Configure<PassportOcrSettings>(configuration.GetSection("PassportOcr"));
        services.Configure<SeedSettings>(configuration.GetSection("SeedSettings"));
        services.Configure<PaymobSettings>(configuration.GetSection("Paymob"));


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
                var baseUrl = aviationEdge.BaseUrl.EndsWith("/") ? aviationEdge.BaseUrl : $"{aviationEdge.BaseUrl}/";
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(aviationEdge.TimeoutSeconds);
            }
        });

        // Geocoding Http Client
        services.AddHttpClient("NominatimGeocoding", client =>
        {
            var geocodingSettings = configuration.GetSection("Geocoding").Get<Travora.Infrastructure.Configurations.GeocodingSettings>();
            if (geocodingSettings != null)
            {
                client.BaseAddress = new Uri(geocodingSettings.BaseUrl);
                client.DefaultRequestHeaders.Add("User-Agent", geocodingSettings.UserAgent);
            }
        });

        // Paymob Http Client
        services.AddHttpClient("Paymob", client =>
        {
            var paymobSettings = configuration.GetSection("Paymob").Get<PaymobSettings>();
            if (paymobSettings != null)
            {
                client.BaseAddress = new Uri(paymobSettings.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            }
        });

        // Aviation Weather Http Client
        services.AddHttpClient("AviationWeather", client =>
        {
            var weatherSettings = configuration.GetSection("AviationWeather").Get<AviationWeatherSettings>();
            if (weatherSettings != null)
            {
                var baseUrl = weatherSettings.BaseUrl.EndsWith("/") ? weatherSettings.BaseUrl : $"{weatherSettings.BaseUrl}/";
                client.BaseAddress = new Uri(baseUrl);
            }
            client.DefaultRequestHeaders.Add("User-Agent", "Travora/1.0");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Register External Services
        services.AddScoped<Travora.Application.Interfaces.External.IAirlineService, Travora.Infrastructure.ExternalServices.Communication.AirlineService>();
        services.AddScoped<Travora.Application.Interfaces.External.IGeocodingService, Travora.Infrastructure.ExternalServices.Communication.NominatimGeocodingService>();

        // Aviation Weather Services
        services.AddScoped<Travora.Application.Interfaces.External.Weather.IAviationWeatherService, Travora.Infrastructure.ExternalServices.Weather.AviationWeatherApiService>();
        services.AddScoped<Travora.Application.Interfaces.External.Weather.IWeatherCache, Travora.Infrastructure.Caching.WeatherCacheService>();
        services.AddScoped<IAirportDetailsService, Travora.Infrastructure.Services.AirportDetailsService>();
        services.AddScoped<IFlightTrackerService, Travora.Infrastructure.Services.FlightTrackerService>();
        
        // Register Draft Order Service (Redis)
        services.AddScoped<Travora.Application.Interfaces.Services.IDraftOrderService, Travora.Infrastructure.Services.DraftOrderService>();

        // Hangfire (Background jobs)
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString));
            
        services.AddHangfireServer();

        return services;
    }
}
