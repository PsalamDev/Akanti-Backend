using Akanti.API.DTOs.Dashboard;

namespace Akanti.API.Interfaces;

public interface IDashboardService
{
    Task<DashboardDto> GetDashboardAsync(int userId);
}
