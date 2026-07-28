using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Expense;
using Akanti.API.Models;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpenseController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ExpenseController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<ExpenseDto>>> GetAll([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] int? categoryId)
    {
        var query = _context.Expenses
            .Where(e => e.UserId == GetUserId() && !e.IsDeleted)
            .Include(e => e.Category)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(e => e.Date >= startDate.Value);
        if (endDate.HasValue) query = query.Where(e => e.Date <= endDate.Value);
        if (categoryId.HasValue) query = query.Where(e => e.CategoryId == categoryId.Value);

        var expenses = await query.OrderByDescending(e => e.Date).ToListAsync();

        return Ok(expenses.Select(e => new ExpenseDto
        {
            Id = e.Id,
            Title = e.Title,
            Description = e.Description,
            Amount = e.Amount,
            CategoryId = e.CategoryId,
            CategoryName = e.Category?.Name,
            Date = e.Date,
            Frequency = e.Frequency.ToString(),
            ReceiptUrl = e.ReceiptUrl,
            CreatedAt = e.CreatedAt
        }));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ExpenseDto>> GetById(int id)
    {
        var expense = await _context.Expenses
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == GetUserId() && !e.IsDeleted);

        if (expense == null) return NotFound();

        return Ok(new ExpenseDto
        {
            Id = expense.Id,
            Title = expense.Title,
            Description = expense.Description,
            Amount = expense.Amount,
            CategoryId = expense.CategoryId,
            CategoryName = expense.Category?.Name,
            Date = expense.Date,
            Frequency = expense.Frequency.ToString(),
            ReceiptUrl = expense.ReceiptUrl,
            CreatedAt = expense.CreatedAt
        });
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Create([FromBody] CreateExpenseRequest request)
    {
        var expense = new Expense
        {
            UserId = GetUserId(),
            Title = request.Title,
            Description = request.Description,
            Amount = request.Amount,
            CategoryId = request.CategoryId,
            Date = request.Date,
            Frequency = Enum.TryParse<ExpenseFrequency>(request.Frequency, true, out var freq) ? freq : ExpenseFrequency.OneTime,
            ReceiptUrl = request.ReceiptUrl
        };

        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = expense.Id }, new ExpenseDto
        {
            Id = expense.Id,
            Title = expense.Title,
            Description = expense.Description,
            Amount = expense.Amount,
            CategoryId = expense.CategoryId,
            Date = expense.Date,
            Frequency = expense.Frequency.ToString(),
            ReceiptUrl = expense.ReceiptUrl,
            CreatedAt = expense.CreatedAt
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateExpenseRequest request)
    {
        var expense = await _context.Expenses
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == GetUserId() && !e.IsDeleted);

        if (expense == null) return NotFound();

        expense.Title = request.Title;
        expense.Description = request.Description;
        expense.Amount = request.Amount;
        expense.CategoryId = request.CategoryId;
        expense.Date = request.Date;
        expense.Frequency = Enum.TryParse<ExpenseFrequency>(request.Frequency, true, out var freq) ? freq : ExpenseFrequency.OneTime;
        expense.ReceiptUrl = request.ReceiptUrl;
        expense.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var expense = await _context.Expenses
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == GetUserId() && !e.IsDeleted);

        if (expense == null) return NotFound();

        expense.IsDeleted = true;
        expense.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("total")]
    public async Task<ActionResult<object>> GetTotal([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var query = _context.Expenses
            .Where(e => e.UserId == GetUserId() && !e.IsDeleted)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(e => e.Date >= startDate.Value);
        if (endDate.HasValue) query = query.Where(e => e.Date <= endDate.Value);

        var total = await query.SumAsync(e => e.Amount);
        return Ok(new { total });
    }
}
