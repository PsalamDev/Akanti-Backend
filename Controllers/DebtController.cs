using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.DTOs.Debt;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DebtController : ControllerBase
{
    private readonly IDebtService _debtService;

    public DebtController(IDebtService debtService)
    {
        _debtService = debtService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<DebtDto>>> GetAll([FromQuery] string? type)
    {
        var debts = await _debtService.GetAllAsync(GetUserId(), type);
        return Ok(debts);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DebtDto>> GetById(int id)
    {
        var debt = await _debtService.GetByIdAsync(GetUserId(), id);
        if (debt == null) return NotFound();
        return Ok(debt);
    }

    [HttpPost]
    public async Task<ActionResult<DebtDto>> Create([FromBody] CreateDebtRequest request)
    {
        var debt = await _debtService.CreateAsync(GetUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = debt.Id }, debt);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateDebtRequest request)
    {
        var updated = await _debtService.UpdateAsync(GetUserId(), id, request);
        if (!updated) return NotFound();
        return NoContent();
    }

    [HttpPost("{id}/payment")]
    public async Task<IActionResult> RecordPayment(int id, [FromBody] UpdateDebtPaymentRequest request)
    {
        var result = await _debtService.RecordPaymentAsync(GetUserId(), id, request);
        if (result == null) return NotFound();
        return Ok(new { result.Value.Id, result.Value.AmountPaid, result.Value.Status });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _debtService.DeleteAsync(GetUserId(), id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<List<DebtDto>>> GetUpcomingDebts([FromQuery] int days = 30)
    {
        var debts = await _debtService.GetUpcomingAsync(GetUserId(), days);
        return Ok(debts);
    }

    [HttpPost("{id}/remind")]
    public async Task<IActionResult> SendReminder(int id)
    {
        await _debtService.SendReminderAsync(GetUserId(), id);
        return Ok(new { message = "Reminder sent successfully." });
    }
}
