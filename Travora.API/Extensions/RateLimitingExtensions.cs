using System.Threading.RateLimiting;

namespace Travora.API.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddRateLimitingPolicies(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // حماية عامة: 100 طلب / دقيقة لكل IP
            options.AddPolicy("general", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // حماية الـ Login: 5 محاولات / دقيقة لكل IP (ضد Brute Force)
            options.AddPolicy("auth", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // الرسالة لما يتعدا الحد
            options.RejectionStatusCode = 429;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"status\":429,\"message\":\"Too many requests. Please try again later.\"}",
                    cancellationToken);
            };
        });

        return services;
    }
}
