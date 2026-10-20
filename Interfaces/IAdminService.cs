using Akanti.API.DTOs.Admin;

namespace Akanti.API.Interfaces;

public interface IAdminService
{
    Task<AdminDashboardDto> GetDashboardAsync();
    Task<PagedResult<AdminUserDto>> GetUsersAsync(string? search, int page, int pageSize);
    Task<AdminUserDetailDto?> GetUserDetailAsync(int id);
    Task<AdminUserTotalsDto?> GetUserTotalsAsync(int id);
    Task<(int Id, bool IsActive)?> ToggleActiveAsync(int id);
    Task<List<AdminAuditLogDto>> GetAuditLogsAsync(int page, int pageSize);
}
