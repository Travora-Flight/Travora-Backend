using Travora.Application.DTOs.Admin.Employees;

namespace Travora.Application.Interfaces;

public interface IAdminEmployeeService
{
    Task<EmployeePagedResponse> GetEmployeesAsync(string? search, int page, int pageSize);
    Task<EmployeeProfileResponse> GetEmployeeProfileAsync(int employeeId);
    Task<CreateEmployeeResponse> CreateEmployeeAsync(int adminId, CreateEmployeeRequest request);
    Task<EmployeeProfileResponse> UpdateEmployeeAsync(int employeeId, UpdateEmployeeRequest request);
    Task<bool> UpdateEmployeeStatusAsync(int employeeId, EmployeeStatusRequest request);
    Task<bool> DeleteEmployeeAsync(int employeeId);
}
