using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.DTOs.Budget;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BudgetController : ControllerBase
{
    private readonly IBudgetService _budgetService;

    public BudgetController(IBudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<BudgetDto>>> GetAll()
    {
        var budgets = await _budgetService.GetAllAsync(GetUserId());
        return Ok(budgets);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BudgetDto>> GetById(int id)
    {
        var budget = await _budgetService.GetByIdAsync(GetUserId(), id);
        if (budget == null) return NotFound();
        return Ok(budget);
    }

    [HttpPost]
    public async Task<ActionResult<BudgetDto>> Create([FromBody] CreateBudgetRequest request)
    {
        var budget = await _budgetService.CreateAsync(GetUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = budget.Id }, budget);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateBudgetRequest request)
    {
        var updated = await _budgetService.UpdateAsync(GetUserId(), id, request);
        if (!updated) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _budgetService.DeleteAsync(GetUserId(), id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<List<BudgetDto>>> GetBudgetAlerts()
    {
        var alerts = await _budgetService.GetAlertsAsync(GetUserId());


        return Ok(alerts);
    }
}
