using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Filters;

namespace Travora.API.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.ExampleFilters(); // <--- Added for Swashbuckle Examples

            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Travora API",
                Version = "v1",
                Contact = new OpenApiContact
                {
                    Name = "Travora Team",
                    Email = "support@travora.com"
                }
            });
            // Avoid duplicate schema ID conflicts between namespaces
            options.CustomSchemaIds(type => type.FullName?.Replace("Travora.Application.DTOs.", "").Replace(".", "_") ?? type.Name);
            options.OrderActionsBy(apiDesc =>
            {
                // Get the controller name
                var controllerName = apiDesc.ActionDescriptor.RouteValues["controller"];

                // If the controller name is Auth, give it priority "0" to appear at the top
                if (controllerName == "Auth")
                {
                    return "0";
                }

                // For any other controller, give it "1" followed by its name, so they appear below and are sorted alphabetically
                return $"1_{controllerName}";
            });

            // Configure JWT in Swagger
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "{your_token}"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        // Register the swagger example providers from the same assembly as this extension
        services.AddSwaggerExamplesFromAssemblyOf<Program>();

        return services;
    }
}
