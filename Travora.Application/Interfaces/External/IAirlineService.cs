using Travora.Application.DTOs.External.Airline;

namespace Travora.Application.Interfaces.External;

public interface IAirlineService
{
    Task<AirlineValidateTicketResponse> ValidateTicketAsync(AirlineValidateTicketRequest request, CancellationToken cancellationToken = default);
    Task<AirlineBaggageCheckResponse> GetBaggageCountAsync(string ticketNumber, CancellationToken cancellationToken = default);
    Task<AirlineCustomsLookupResponse> LookupCustomsProductAsync(string productName, CancellationToken cancellationToken = default);
    Task<AirlineCustomsLookupResponse> GetCustomsRateAsync(string categoryName, string productName, CancellationToken cancellationToken = default);
    Task<List<AirlineCustomsCategoryResponse>> GetCustomsCategoriesAsync(CancellationToken cancellationToken = default);
    Task<AirlineBaggageByTicketResponse> GetBaggageByTicketAsync(string ticketNumber, CancellationToken cancellationToken = default);
    Task<AirlineIssueBoardingPassResponse> IssueBoardingPassAsync(string ticketNumber, CancellationToken cancellationToken = default);
    Task<AirlineBaggageAllowanceResponse> GetBaggageAllowanceAsync(string ticketNumber, CancellationToken cancellationToken = default);
}
