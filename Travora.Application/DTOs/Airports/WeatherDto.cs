namespace Travora.Application.DTOs.Airports;

public class WeatherDto
{
    public decimal Temperature { get; set; }
    public decimal FeelsLike { get; set; }
    public int WindDirection { get; set; }
    public decimal WindSpeed { get; set; }
    public string Visibility { get; set; } = string.Empty;
    public decimal Pressure { get; set; } // pressure_mb
    public int Humidity { get; set; }
    
    public string ConditionText { get; set; } = string.Empty;
    public string ConditionIcon { get; set; } = string.Empty;
    public int ConditionCode { get; set; }
    
    public string Sunrise { get; set; } = string.Empty;
    public string Sunset { get; set; } = string.Empty;
    public int ChanceOfRain { get; set; }
    public decimal MaxTemp { get; set; }
    public decimal MinTemp { get; set; }
    
    public DateTime ReportTime { get; set; }
}
