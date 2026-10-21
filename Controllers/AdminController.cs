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
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IAdminService adminService,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<AdminController> logger)
    {
        _adminService = adminService;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Sends a test email synchronously and reports the provider's actual response.
    ///
    /// Transactional mail is queued in the background, so a failure there is only visible
    /// in server logs. This endpoint performs the send inline and surfaces the real error,
    /// which makes a blocked provider, a rejected sender, or a misnamed environment
    /// variable diagnosable in one request instead of by log triage.
    /// </summary>
    [HttpPost("test-email")]
    public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest request)
    {
        var provider = (_configuration["Email:Provider"] ?? "").Trim();
        var from = _configuration["Email:FromEmail"] ?? _configuration["Smtp:FromEmail"] ?? "";
        var keyConfigured = !string.IsNullOrWhiteSpace(_configuration["Email:ApiKey"]);

        try
        {
            await _emailService.SendAsync(
                request.To,
                "Akanti test email",
                "<p>This is a test message from Akanti. If you received it, transactional email is working.</p>");

            return Ok(new
            {
                sent = true,
                provider = provider == "" ? "smtp (no Email:Provider set)" : provider,
                from,
                message = "Provider accepted the message. Check the inbox and the spam folder."
            });
        }
        catch (Exception ex)
        {
            // The exception message carries the provider's own reason. Log the stack,
            // return the message, since this endpoint exists purely for diagnosis.
            _logger.LogError(ex, "Test email to {To} failed", request.To);

            return StatusCode(500, new
            {
                sent = false,
                provider = provider == "" ? "smtp (no Email:Provider set)" : provider,
                from,
                apiKeyConfigured = keyConfigured,
                error = ex.Message
            });
        }
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
