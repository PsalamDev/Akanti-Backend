using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.DTOs.Expense;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpenseController : ControllerBase
{
    private readonly IExpenseService _expenseService;

    public ExpenseController(IExpenseService expenseService)
    {
        _expenseService = expenseService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<ExpenseDto>>> GetAll([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] int? categoryId)
    {
        var expenses = await _expenseService.GetAllAsync(GetUserId(), startDate, endDate, categoryId);
        return Ok(expenses);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ExpenseDto>> GetById(int id)
    {
        var expense = await _expenseService.GetByIdAsync(GetUserId(), id);
        if (expense == null) return NotFound();
        return Ok(expense);
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Create([FromBody] CreateExpenseRequest request)
    {
        var expense = await _expenseService.CreateAsync(GetUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = expense.Id }, expense);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateExpenseRequest request)
    {
        var updated = await _expenseService.UpdateAsync(GetUserId(), id, request);
        if (!updated) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _expenseService.DeleteAsync(GetUserId(), id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpGet("total")]
    public async Task<ActionResult<object>> GetTotal([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var total = await _expenseService.GetTotalAsync(GetUserId(), startDate, endDate);
        return Ok(new { total });
    }
}
