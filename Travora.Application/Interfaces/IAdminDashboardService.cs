using Travora.Application.DTOs.Admin.Dashboard;

namespace Travora.Application.Interfaces;

public interface IAdminDashboardService
{
    Task<DashboardStatsResponse> GetDashboardStatsAsync();
}
