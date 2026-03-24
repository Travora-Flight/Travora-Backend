using System.Net.Http.Json;
using Travora.Application.DTOs.External.Airline;
using Travora.Application.Interfaces.External;

namespace Travora.Infrastructure.ExternalServices.Communication;

public class AirlineService : IAirlineService
{
    private readonly HttpClient _httpClient;

    public AirlineService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("AirlineApi");
    }

    public async Task<AirlineValidateTicketResponse> ValidateTicketAsync(AirlineValidateTicketRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/airline/validate-ticket", request, cancellationToken);
        
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            return new AirlineValidateTicketResponse 
            { 
                IsValid = false,
                Errors = new List<string> { $"Airline API error {(int)response.StatusCode}: {rawJson}" }
            };
        }
        
        var result = System.Text.Json.JsonSerializer.Deserialize<AirlineValidateTicketResponse>(
            rawJson, 
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );
        
        var flightData = result?.Flight ?? result?.Ticket?.Flight ?? result?.FlightInfo;
        var passengerData = result?.Passenger ?? result?.Ticket?.Passenger ?? result?.PassengerInfo;

        if (result != null && (!result.IsValid || flightData == null || passengerData == null))
        {
            result.Errors ??= new List<string>();
            result.Errors.Add($"Raw Airline API Response: {rawJson}");
        }
        
        return result ?? new AirlineValidateTicketResponse { IsValid = false, Errors = new List<string> { "Empty response from airline." } };
    }

    public async Task<AirlineBaggageCheckResponse> GetBaggageCountAsync(string ticketNumber, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/airline/baggage-check?ticketNumber={Uri.EscapeDataString(ticketNumber)}", cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            return new AirlineBaggageCheckResponse { TicketNumber = ticketNumber, TotalBaggageCount = 0 };
        }

        var result = await response.Content.ReadFromJsonAsync<AirlineBaggageCheckResponse>(cancellationToken: cancellationToken);
        return result ?? new AirlineBaggageCheckResponse { TicketNumber = ticketNumber, TotalBaggageCount = 0 };
    }

    public async Task<AirlineCustomsLookupResponse> LookupCustomsProductAsync(string productName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/customs/lookup?productName={Uri.EscapeDataString(productName)}", cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            return new AirlineCustomsLookupResponse { Found = false };
        }

        var result = await response.Content.ReadFromJsonAsync<AirlineCustomsLookupResponse>(cancellationToken: cancellationToken);
        return result ?? new AirlineCustomsLookupResponse { Found = false };
    }

    public async Task<AirlineBaggageByTicketResponse> GetBaggageByTicketAsync(string ticketNumber, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/airline/baggage/by-ticket/{Uri.EscapeDataString(ticketNumber)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
            return new AirlineBaggageByTicketResponse { TicketNumber = ticketNumber };

        var result = await response.Content.ReadFromJsonAsync<AirlineBaggageByTicketResponse>(cancellationToken: cancellationToken);
        return result ?? new AirlineBaggageByTicketResponse { TicketNumber = ticketNumber };
    }

    public async Task<AirlineIssueBoardingPassResponse> IssueBoardingPassAsync(string ticketNumber, CancellationToken cancellationToken = default)
    {
        var request = new AirlineIssueBoardingPassRequest { TicketNumber = ticketNumber };
        var response = await _httpClient.PostAsJsonAsync("/api/airline/issue-boarding-pass", request, cancellationToken);

        response.EnsureSuccessStatusCode();

        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = await response.Content.ReadFromJsonAsync<AirlineIssueBoardingPassResponse>(options, cancellationToken: cancellationToken);
        return result ?? new AirlineIssueBoardingPassResponse();
    }
}
