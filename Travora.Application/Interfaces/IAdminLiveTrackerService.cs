using Travora.Application.DTOs.Admin.LiveTracker;

namespace Travora.Application.Interfaces;

public interface IAdminLiveTrackerService
{
    Task<LiveEmployeeResponse> GetLastLocationsAsync(string? filter, string? search);
    Task<EmployeeLocationDetailResponse> GetEmployeeLocationDetailsAsync(int employeeId);
}
