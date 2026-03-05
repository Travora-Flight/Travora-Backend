using Travora.Application.DTOs.Admin.Dashboard;

namespace Travora.Application.Interfaces;

public interface IAdminDashboardService
{
    Task<DashboardStatsResponse> GetDashboardStatsAsync();
    Task<OnlineEmployeesResponse> GetOnlineEmployeesAsync();
    Task<RecentOrdersResponse> GetRecentOrdersAsync(int take = 10);
    Task<LiveLocationsResponse> GetLiveLocationsAsync();
}
