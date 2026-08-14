using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.DTOs.Report;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportController(IReportService reportService)
    {
        _reportService = reportService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<ReportDto>> GetReport([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var report = await _reportService.GetReportAsync(GetUserId(), startDate, endDate);
        return Ok(report);
    }

    [HttpGet("profit-loss")]
    public async Task<ActionResult<ProfitLossDto>> GetProfitLoss([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var profitLoss = await _reportService.GetProfitLossAsync(GetUserId(), startDate, endDate);
        return Ok(profitLoss);
    }
}
