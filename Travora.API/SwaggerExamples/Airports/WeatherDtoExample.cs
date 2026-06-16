using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Airports;

namespace Travora.API.SwaggerExamples.Airports;

public class WeatherDtoExample : IExamplesProvider<WeatherDto>
{
    public WeatherDto GetExamples()
    {
        return new WeatherDto
        {
            Temperature = 32.1m,
            FeelsLike = 29.9m,
            WindDirection = 307,
            WindSpeed = 14.8m,
            Visibility = "10",
            Pressure = 1012m,
            Humidity = 32,
            ConditionText = "Sunny",
            ConditionIcon = "https://cdn.weatherapi.com/weather/64x64/day/113.png",
            ConditionCode = 1000,
            Sunrise = "05:52 AM",
            Sunset = "07:57 PM",
            ChanceOfRain = 0,
            MaxTemp = 35.0m,
            MinTemp = 24.0m,
            ReportTime = DateTime.UtcNow
        };
    }
}
