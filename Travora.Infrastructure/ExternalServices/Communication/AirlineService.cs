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
        
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = System.Text.Json.JsonSerializer.Deserialize<AirlineValidateTicketResponse>(rawJson, options);
        
        var flightData = result?.Flight ?? result?.Ticket?.Flight ?? result?.FlightInfo;
        var passengerData = result?.Passenger ?? result?.Ticket?.Passenger ?? result?.PassengerInfo;

        if (result != null && (!result.IsValid || flightData == null || passengerData == null))
        {
            result.Errors ??= new List<string>();
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

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = System.Text.Json.JsonSerializer.Deserialize<AirlineBaggageCheckResponse>(json, options);

        return result ?? new AirlineBaggageCheckResponse { TicketNumber = ticketNumber, TotalBaggageCount = 0 };
    }

    public async Task<AirlineCustomsLookupResponse> LookupCustomsProductAsync(string productName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/customs/lookup?productName={Uri.EscapeDataString(productName)}", cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            return new AirlineCustomsLookupResponse { Found = false };
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = System.Text.Json.JsonSerializer.Deserialize<AirlineCustomsLookupResponse>(json, options);

        return result ?? new AirlineCustomsLookupResponse { Found = false };
    }

    public async Task<AirlineBaggageByTicketResponse> GetBaggageByTicketAsync(string ticketNumber, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/airline/baggage/by-ticket/{Uri.EscapeDataString(ticketNumber)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
            return new AirlineBaggageByTicketResponse { TicketNumber = ticketNumber };

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = System.Text.Json.JsonSerializer.Deserialize<AirlineBaggageByTicketResponse>(json, options);

        return result ?? new AirlineBaggageByTicketResponse { TicketNumber = ticketNumber };
    }

    public async Task<AirlineIssueBoardingPassResponse> IssueBoardingPassAsync(string ticketNumber, CancellationToken cancellationToken = default)
    {
        var request = new AirlineIssueBoardingPassRequest { TicketNumber = ticketNumber };
        var response = await _httpClient.PostAsJsonAsync("/api/airline/issue-boarding-pass", request, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // Try the wrapper first
        var wrapper = System.Text.Json.JsonSerializer.Deserialize<AirlineIssueBoardingPassWrapper>(json, options);
        var result = wrapper?.BoardingPasses?.FirstOrDefault();

        // If not a wrapper, try direct object
        if (result == null)
            result = System.Text.Json.JsonSerializer.Deserialize<AirlineIssueBoardingPassResponse>(json, options);

        return result ?? new AirlineIssueBoardingPassResponse();
    }

    public async Task<AirlineBaggageAllowanceResponse> GetBaggageAllowanceAsync(string ticketNumber, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/airline/tickets/{Uri.EscapeDataString(ticketNumber)}/baggage-allowance", cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            return new AirlineBaggageAllowanceResponse { TicketNumber = ticketNumber, AllowedBaggageCount = 0 };
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = System.Text.Json.JsonSerializer.Deserialize<AirlineBaggageAllowanceResponse>(json, options);

        return result ?? new AirlineBaggageAllowanceResponse { TicketNumber = ticketNumber, AllowedBaggageCount = 0 };
    }
}
