using Travora.Application.DTOs.Employee.Dashboard;

namespace Travora.Application.Interfaces.Services.Employee;

public interface IEmployeeDashboardService
{
    Task<EmployeeDashboardResponse> GetDashboardAsync(int employeeId);
}
