using System.Text.Json.Serialization;

namespace Travora.Application.DTOs.Flights;

public class DelayPredictionRequestDto
{
    [JsonPropertyName("departure")]
    public PredictionDepartureDto Departure { get; set; } = null!;

    [JsonPropertyName("arrival")]
    public PredictionArrivalDto Arrival { get; set; } = null!;

    [JsonPropertyName("weather")]
    public PredictionWeatherDto Weather { get; set; } = null!;

    [JsonPropertyName("OriginTotalTrafficHour")]
    public double OriginTotalTrafficHour { get; set; }

    [JsonPropertyName("OriginHistAvgCongestion")]
    public double OriginHistAvgCongestion { get; set; }
}

public class PredictionDepartureDto
{
    [JsonPropertyName("scheduledDeparture")]
    public string ScheduledDeparture { get; set; } = string.Empty;

    [JsonPropertyName("iataCode")]
    public string IataCode { get; set; } = string.Empty;
}

public class PredictionArrivalDto
{
    [JsonPropertyName("iataCode")]
    public string IataCode { get; set; } = string.Empty;
}

public class PredictionWeatherDto
{
    [JsonPropertyName("tempF")]
    public double TempF { get; set; }

    [JsonPropertyName("WindChillF")]
    public double WindChillF { get; set; }

    [JsonPropertyName("humidity")]
    public double Humidity { get; set; }

    [JsonPropertyName("windspeedKmph")]
    public double WindspeedKmph { get; set; }

    [JsonPropertyName("WindGustKmph")]
    public double WindGustKmph { get; set; }

    [JsonPropertyName("winddirDegree")]
    public double WinddirDegree { get; set; }

    [JsonPropertyName("weatherCode")]
    public double WeatherCode { get; set; }

    [JsonPropertyName("precipMM")]
    public double PrecipMM { get; set; }

    [JsonPropertyName("visibility")]
    public double Visibility { get; set; }

    [JsonPropertyName("pressure")]
    public double Pressure { get; set; }

    [JsonPropertyName("cloudcover")]
    public double Cloudcover { get; set; }

    [JsonPropertyName("DewPointF")]
    public double DewPointF { get; set; }
}

public class DelayPredictionResponseDto
{
    [JsonPropertyName("predicted_delay_minutes")]
    public double PredictedDelayMinutes { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public class AirlineSimulationFeaturesResponseDto
{
    public string FlightNumber { get; set; } = string.Empty;
    public string DepartureIataCode { get; set; } = string.Empty;
    public DateTime ScheduledDepartureUtc { get; set; }
    public double OriginTotalTrafficHour { get; set; }
    public double OriginHistAvgCongestion { get; set; }
}
