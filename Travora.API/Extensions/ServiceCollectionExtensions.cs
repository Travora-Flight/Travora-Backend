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
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;

namespace Travora.API.Extensions;

using Travora.Application.Interfaces.Services.Employee;

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

        // Redis
        var redisSettings = configuration.GetSection("RedisSettings").Get<RedisSettings>();
        if (redisSettings != null)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisSettings.ConnectionString;
                options.InstanceName = redisSettings.InstanceName;
            });
            
            // Register IConnectionMultiplexer directly for StackExchange.Redis usage
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp => 
                StackExchange.Redis.ConnectionMultiplexer.Connect(redisSettings.ConnectionString));
        }

        // JWT Token Generator + Auth Service
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
        services.AddSingleton<IJwtTokenGenerator>(new JwtTokenGenerator(jwtSettings));
        services.AddScoped<IAuthService, AuthService>();

        // Admin Services
        services.AddScoped<IAdminAccountService, Travora.Infrastructure.AdminPanel.Services.AdminAccountService>();
        services.AddScoped<IAdminSettingsService, Travora.Infrastructure.AdminPanel.Services.AdminSettingsService>();
        services.AddScoped<IAdminDashboardService, Travora.Infrastructure.AdminPanel.Services.AdminDashboardService>();
        services.AddScoped<IAdminEmployeeService, Travora.Infrastructure.AdminPanel.Services.AdminEmployeeService>();
        services.AddScoped<IAdminRequestService, Travora.Infrastructure.AdminPanel.Services.AdminRequestService>();
        services.AddScoped<IAdminLiveTrackerService, Travora.Infrastructure.AdminPanel.Services.AdminLiveTrackerService>();
        services.AddScoped<IAdminPassportService, Travora.Infrastructure.AdminPanel.Services.AdminPassportService>();
        services.AddScoped<IAdminPricingService, Travora.Infrastructure.AdminPanel.Services.AdminPricingService>();
        services.AddScoped<IAdminReportService, Travora.Infrastructure.AdminPanel.Services.AdminReportService>();
        services.AddScoped<IReportGeneratorJob, Travora.Infrastructure.BackgroundJobs.ReportGeneratorJob>();

        // Employee Services
        services.AddScoped<IEmployeeDashboardService, Travora.Infrastructure.EmployeePanel.Services.EmployeeDashboardService>();
        services.AddScoped<IEmployeeTaskService, Travora.Infrastructure.EmployeePanel.Services.EmployeeTaskService>();
        services.AddScoped<IEmployeeBaggageService, Travora.Infrastructure.EmployeePanel.Services.EmployeeBaggageService>();
        services.AddScoped<IEmployeeLocationService, Travora.Infrastructure.EmployeePanel.Services.EmployeeLocationService>();
        services.AddScoped<IEmployeeNotificationService, Travora.Infrastructure.EmployeePanel.Services.EmployeeNotificationService>();
        services.AddScoped<IEmployeeAccountService, Travora.Infrastructure.EmployeePanel.Services.EmployeeAccountService>();

        // Hub Services
        services.AddScoped<Travora.Application.Interfaces.Hubs.ILiveTrackingHubService, Travora.API.Services.LiveTrackingHubService>();

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
