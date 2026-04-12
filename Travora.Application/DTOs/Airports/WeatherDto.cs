 namespace Travora.Application.DTOs.Airports;

public class WeatherDto
{
    public decimal Temperature { get; set; }
    public decimal Dewpoint { get; set; }
    public int WindDirection { get; set; }
    public decimal WindSpeed { get; set; }
    public string Visibility { get; set; } = string.Empty;
    public decimal Altimeter { get; set; }
    public string CloudCover { get; set; } = string.Empty;
    public string FlightCategory { get; set; } = string.Empty;
    public string MetarType { get; set; } = string.Empty;
    public string RawObservation { get; set; } = string.Empty;
    public int Elevation { get; set; }
    public DateTime ReportTime { get; set; }
    public List<CloudLayerDto> CloudLayers { get; set; } = new();
}
