using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Budget;
using Akanti.API.Models;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BudgetController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public BudgetController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<BudgetDto>>> GetAll()
    {
        var budgets = await _context.Budgets
            .Where(b => b.UserId == GetUserId())
            .Include(b => b.Category)
            .ToListAsync();

        return Ok(budgets.Select(b => new BudgetDto
        {
            Id = b.Id,
            Name = b.Name,
            Amount = b.Amount,
            Spent = b.Spent,
            CategoryId = b.CategoryId,
            CategoryName = b.Category?.Name,
            Period = b.Period.ToString(),
            StartDate = b.StartDate,
            EndDate = b.EndDate,
            IsActive = b.IsActive,
            AlertThresholdPercentage = b.AlertThresholdPercentage
        }));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BudgetDto>> GetById(int id)
    {
        var budget = await _context.Budgets
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == GetUserId());

        if (budget == null) return NotFound();

        return Ok(new BudgetDto
        {
            Id = budget.Id,
            Name = budget.Name,
            Amount = budget.Amount,
            Spent = budget.Spent,
            CategoryId = budget.CategoryId,
            CategoryName = budget.Category?.Name,
            Period = budget.Period.ToString(),
            StartDate = budget.StartDate,
            EndDate = budget.EndDate,
            IsActive = budget.IsActive,
            AlertThresholdPercentage = budget.AlertThresholdPercentage
        });
    }

    [HttpPost]
    public async Task<ActionResult<BudgetDto>> Create([FromBody] CreateBudgetRequest request)
    {
        var budget = new Budget
        {
            UserId = GetUserId(),
            Name = request.Name,
            Amount = request.Amount,
            CategoryId = request.CategoryId,
            Period = Enum.TryParse<BudgetPeriod>(request.Period, true, out var p) ? p : BudgetPeriod.Monthly,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            AlertThresholdPercentage = request.AlertThresholdPercentage
        };

        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = budget.Id }, new BudgetDto
        {
            Id = budget.Id,
            Name = budget.Name,
            Amount = budget.Amount,
            Spent = budget.Spent,
            CategoryId = budget.CategoryId,
            Period = budget.Period.ToString(),
            StartDate = budget.StartDate,
            EndDate = budget.EndDate,
            IsActive = budget.IsActive,
            AlertThresholdPercentage = budget.AlertThresholdPercentage
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateBudgetRequest request)
    {
        var budget = await _context.Budgets
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == GetUserId());

        if (budget == null) return NotFound();

        budget.Name = request.Name;
        budget.Amount = request.Amount;
        budget.CategoryId = request.CategoryId;
        budget.Period = Enum.TryParse<BudgetPeriod>(request.Period, true, out var p) ? p : BudgetPeriod.Monthly;
        budget.StartDate = request.StartDate;
        budget.EndDate = request.EndDate;
        budget.AlertThresholdPercentage = request.AlertThresholdPercentage;
        budget.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var budget = await _context.Budgets
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == GetUserId());

        if (budget == null) return NotFound();

        _context.Budgets.Remove(budget);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<List<BudgetDto>>> GetBudgetAlerts()
    {
        var budgets = await _context.Budgets
            .Where(b => b.UserId == GetUserId() && b.IsActive)
            .ToListAsync();

        var alerts = budgets
            .Where(b => b.Amount > 0 && (b.Spent / b.Amount) * 100 >= b.AlertThresholdPercentage)
            .Select(b => new BudgetDto
            {
                Id = b.Id,
                Name = b.Name,
                Amount = b.Amount,
                Spent = b.Spent,
                CategoryId = b.CategoryId,
                Period = b.Period.ToString(),
                StartDate = b.StartDate,
                EndDate = b.EndDate,
                IsActive = b.IsActive,
                AlertThresholdPercentage = b.AlertThresholdPercentage
            })
            .ToList();

        return Ok(alerts);
    }
}
