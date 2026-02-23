using Travora.API.Configurations;

namespace Travora.API.Extensions;

public static class CorsExtensions
{
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        var corsSettings = configuration.GetSection("CorsSettings").Get<CorsSettings>();

        services.AddCors(options =>
        {
            options.AddPolicy("TravoraPolicy", builder =>
            {
                builder
                    .WithOrigins(corsSettings?.AllowedOrigins ?? ["http://localhost:3000"])
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        return services;
    }
}
