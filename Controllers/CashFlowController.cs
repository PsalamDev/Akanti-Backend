using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.DTOs.CashFlow;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CashFlowController : ControllerBase
{
    private readonly ICashFlowService _cashFlowService;

    public CashFlowController(ICashFlowService cashFlowService)
    {
        _cashFlowService = cashFlowService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<CashFlowDto>> GetCashFlow([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var cashFlow = await _cashFlowService.GetCashFlowAsync(GetUserId(), startDate, endDate);
        return Ok(cashFlow);
    }

    [HttpGet("monthly")]
    public async Task<ActionResult<List<CashFlowDto>>> GetMonthlyCashFlow([FromQuery] int months = 12)
    {
        var result = await _cashFlowService.GetMonthlyCashFlowAsync(GetUserId(), months);
        return Ok(result);
    }
}
