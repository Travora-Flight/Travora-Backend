using System.Net;
using System.Text.Json;

namespace Travora.API.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Let Swagger errors pass through for debugging
            if (context.Request.Path.StartsWithSegments("/swagger"))
            {
                _logger.LogError(ex, "Swagger error: {Message}", ex.Message);
                throw;
            }
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = exception switch
        {
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, exception.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            InvalidOperationException => (HttpStatusCode.Conflict, exception.Message),
            ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
            _ => (HttpStatusCode.InternalServerError, "An internal error occurred")
        };

        if (statusCode == HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        else
            _logger.LogWarning("Handled exception ({StatusCode}): {Message}", (int)statusCode, exception.Message);

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var response = JsonSerializer.Serialize(new
        {
            status = (int)statusCode,
            message,
            timestamp = DateTime.UtcNow
        });

        await context.Response.WriteAsync(response);
    }
}
