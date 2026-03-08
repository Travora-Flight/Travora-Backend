namespace Travora.Application.DTOs.Admin.Seed;

public class AviationEdgeCountryResponse
{
    public int? CountryId { get; set; }
    public string? NameCountry { get; set; }
    public string? CodeIso2Country { get; set; }
    public string? CodeIso3Country { get; set; }
    public string? NumericIso { get; set; }
    public string? Continent { get; set; }
    public string? Capital { get; set; }
    public string? CodeCurrency { get; set; }
    public string? NameCurrency { get; set; }
    public string? PhonePrefix { get; set; }
    public string? Population { get; set; }
    public string? CodeFips { get; set; }
}

public class AviationEdgeCityResponse
{
    public int? CityId { get; set; }
    public string? NameCity { get; set; }
    public string? CodeIataCity { get; set; }
    public string? CodeIso2Country { get; set; }
    public decimal? LatitudeCity { get; set; }
    public decimal? LongitudeCity { get; set; }
    public string? Timezone { get; set; }
    public string? GMT { get; set; }
    public int? GeonameId { get; set; }
}

public class AviationEdgeAirportResponse
{
    public int? AirportId { get; set; }
    public string? NameAirport { get; set; }
    public string? CodeIataAirport { get; set; }
    public string? CodeIcaoAirport { get; set; }
    public string? CodeIataCity { get; set; }
    public string? CodeIso2Country { get; set; }
    public decimal? LatitudeAirport { get; set; }
    public decimal? LongitudeAirport { get; set; }
    public string? Timezone { get; set; }
    public string? GMT { get; set; }
    public string? GeonameId { get; set; }
    public string? Phone { get; set; }
    public string? NameCountry { get; set; }
}

public class AviationEdgeAirlineResponse
{
    public int? AirlineId { get; set; }
    public string? NameAirline { get; set; }
    public string? CodeIataAirline { get; set; }
    public string? CodeIcaoAirline { get; set; }
    public string? NameCountry { get; set; }
    public string? CodeIso2Country { get; set; }
    public string? Callsign { get; set; }
    public string? CodeHub { get; set; }
    public int? Founding { get; set; }
    public int? SizeAirline { get; set; }
    public decimal? AgeFleet { get; set; }
    public string? IataPrefixAccounting { get; set; }
    public string? Type { get; set; }
    public string? StatusAirline { get; set; }
}

public class AviationEdgeAircraftResponse
{
    public int? AirplaneId { get; set; }
    public string? NumberRegistration { get; set; }
    public string? HexIcaoAirplane { get; set; }
    public string? AirplaneIataType { get; set; }
    public string? CodeIataPlaneLong { get; set; }
    public string? CodeIataPlaneShort { get; set; }
    public string? CodeIataAirline { get; set; }
    public string? CodeIcaoAirline { get; set; }
    public string? ConstructionNumber { get; set; }
    public string? DeliveryDate { get; set; }
    public string? FirstFlight { get; set; }
    public string? LineNumber { get; set; }
    public string? ModelCode { get; set; }
    public string? EnginesCount { get; set; }
    public string? EnginesType { get; set; }
    public string? PlaneAge { get; set; }
    public string? PlaneClass { get; set; }
    public string? PlaneModel { get; set; }
    public string? PlaneSeries { get; set; }
    public string? PlaneOwner { get; set; }
    public string? PlaneStatus { get; set; }
    public string? ProductionLine { get; set; }
    public string? RegistrationDate { get; set; }
    public string? RolloutDate { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("numberTestRgistration")]
    public string? NumberTestRegistration { get; set; }
}
