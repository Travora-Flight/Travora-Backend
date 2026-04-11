namespace Travora.API.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseTravoraMiddleware(this WebApplication app)
    {
        // Swagger في بيئة التطوير فقط
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Travora API v1");
            options.RoutePrefix = "swagger"; 
        });
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }
        app.UseCors("TravoraPolicy");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}
