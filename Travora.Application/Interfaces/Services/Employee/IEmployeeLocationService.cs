using Travora.Application.DTOs.Employee.Location;

namespace Travora.Application.Interfaces.Services.Employee;

public interface IEmployeeLocationService
{
    Task<DriverLocationResponse> UpdateLocationAsync(int employeeId, DriverLocationRequest request);
}
