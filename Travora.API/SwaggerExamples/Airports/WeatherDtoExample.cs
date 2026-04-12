using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Airports;

namespace Travora.API.SwaggerExamples.Airports;

public class WeatherDtoExample : IExamplesProvider<WeatherDto>
{
    public WeatherDto GetExamples()
    {
        return new WeatherDto
        {
            Temperature = 18,
            Dewpoint = 12,
            WindDirection = 60,
            WindSpeed = 10m,
            Visibility = "6+",
            Altimeter = 1018,
            CloudCover = "CAVOK",
            FlightCategory = "VFR",
            MetarType = "METAR",
            RawObservation = "METAR HECA 112100Z 06010KT CAVOK 18/12 Q1018 NOSIG",
            Elevation = 142,
            ReportTime = DateTime.UtcNow,
            CloudLayers = new List<CloudLayerDto>()
        };
    }
}
