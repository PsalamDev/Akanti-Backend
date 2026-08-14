using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.DTOs.Income;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IncomeController : ControllerBase
{
    private readonly IIncomeService _incomeService;

    public IncomeController(IIncomeService incomeService)
    {
        _incomeService = incomeService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<IncomeDto>>> GetAll([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var incomes = await _incomeService.GetAllAsync(GetUserId(), startDate, endDate);
        return Ok(incomes);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<IncomeDto>> GetById(int id)
    {
        var income = await _incomeService.GetByIdAsync(GetUserId(), id);
        if (income == null) return NotFound();
        return Ok(income);
    }

    [HttpPost]
    public async Task<ActionResult<IncomeDto>> Create([FromBody] CreateIncomeRequest request)
    {
        var income = await _incomeService.CreateAsync(GetUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = income.Id }, income);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateIncomeRequest request)
    {
        var updated = await _incomeService.UpdateAsync(GetUserId(), id, request);
        if (!updated) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _incomeService.DeleteAsync(GetUserId(), id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpGet("total")]
    public async Task<ActionResult<object>> GetTotal([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var total = await _incomeService.GetTotalAsync(GetUserId(), startDate, endDate);
        return Ok(new { total });
    }
}
