using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.DTOs.Admin;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardDto>> GetDashboard()
    {
        return Ok(await _adminService.GetDashboardAsync());
    }

    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> GetUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        return Ok(await _adminService.GetUsersAsync(search, page, pageSize));
    }

    [HttpGet("users/{id}/totals")]
    public async Task<ActionResult<AdminUserTotalsDto>> GetUserTotals(int id)
    {
        var totals = await _adminService.GetUserTotalsAsync(id);
        if (totals == null) return NotFound(new { message = "User not found." });
        return Ok(totals);
    }

    [HttpGet("users/{id}")]
    public async Task<ActionResult<AdminUserDetailDto>> GetUserDetail(int id)
    {
        var detail = await _adminService.GetUserDetailAsync(id);
        if (detail == null) return NotFound(new { message = "User not found." });
        return Ok(detail);
    }

    [HttpPut("users/{id}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var result = await _adminService.ToggleActiveAsync(id);
        if (result == null) return NotFound(new { message = "User not found." });
        return Ok(new { result.Value.Id, result.Value.IsActive });
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<List<AdminAuditLogDto>>> GetAuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        return Ok(await _adminService.GetAuditLogsAsync(page, pageSize));
    }
}
