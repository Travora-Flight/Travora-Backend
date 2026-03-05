using Microsoft.OpenApi.Models;

namespace Travora.API.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
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
                // بنجيب اسم الـ Controller
                var controllerName = apiDesc.ActionDescriptor.RouteValues["controller"];

                // لو الكنترولر اسمه Auth، هنديله الأولوية "0" عشان يترسم فوق خالص
                if (controllerName == "Auth")
                {
                    return "0";
                }

                // أي كنترولر تاني هنديله "1" وبعدين اسمه، عشان ينزلوا تحت ويترتبوا أبجدي
                return $"1_{controllerName}";
            });

            // إعداد JWT في Swagger
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "أدخل التوكن بالشكل التالي: {your_token}"
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

        return services;
    }
}
