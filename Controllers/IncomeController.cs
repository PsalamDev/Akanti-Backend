using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Income;
using Akanti.API.Models;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IncomeController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public IncomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<IncomeDto>>> GetAll([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var query = _context.Incomes
            .Where(i => i.UserId == GetUserId() && !i.IsDeleted)
            .Include(i => i.Category)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(i => i.Date >= startDate.Value);
        if (endDate.HasValue) query = query.Where(i => i.Date <= endDate.Value);

        var incomes = await query.OrderByDescending(i => i.Date).ToListAsync();

        return Ok(incomes.Select(i => new IncomeDto
        {
            Id = i.Id,
            Title = i.Title,
            Description = i.Description,
            Amount = i.Amount,
            CategoryId = i.CategoryId,
            CategoryName = i.Category?.Name,
            Date = i.Date,
            Frequency = i.Frequency.ToString(),
            CreatedAt = i.CreatedAt
        }));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<IncomeDto>> GetById(int id)
    {
        var income = await _context.Incomes
            .Include(i => i.Category)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == GetUserId() && !i.IsDeleted);

        if (income == null) return NotFound();

        return Ok(new IncomeDto
        {
            Id = income.Id,
            Title = income.Title,
            Description = income.Description,
            Amount = income.Amount,
            CategoryId = income.CategoryId,
            CategoryName = income.Category?.Name,
            Date = income.Date,
            Frequency = income.Frequency.ToString(),
            CreatedAt = income.CreatedAt
        });
    }

    [HttpPost]
    public async Task<ActionResult<IncomeDto>> Create([FromBody] CreateIncomeRequest request)
    {
        var income = new Income
        {
            UserId = GetUserId(),
            Title = request.Title,
            Description = request.Description,
            Amount = request.Amount,
            CategoryId = request.CategoryId,
            Date = request.Date,
            Frequency = Enum.TryParse<IncomeFrequency>(request.Frequency, true, out var freq) ? freq : IncomeFrequency.OneTime
        };

        _context.Incomes.Add(income);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = income.Id }, new IncomeDto
        {
            Id = income.Id,
            Title = income.Title,
            Description = income.Description,
            Amount = income.Amount,
            CategoryId = income.CategoryId,
            Date = income.Date,
            Frequency = income.Frequency.ToString(),
            CreatedAt = income.CreatedAt
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateIncomeRequest request)
    {
        var income = await _context.Incomes
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == GetUserId() && !i.IsDeleted);

        if (income == null) return NotFound();

        income.Title = request.Title;
        income.Description = request.Description;
        income.Amount = request.Amount;
        income.CategoryId = request.CategoryId;
        income.Date = request.Date;
        income.Frequency = Enum.TryParse<IncomeFrequency>(request.Frequency, true, out var freq) ? freq : IncomeFrequency.OneTime;
        income.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var income = await _context.Incomes
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == GetUserId() && !i.IsDeleted);

        if (income == null) return NotFound();

        income.IsDeleted = true;
        income.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("total")]
    public async Task<ActionResult<object>> GetTotal([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var query = _context.Incomes
            .Where(i => i.UserId == GetUserId() && !i.IsDeleted)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(i => i.Date >= startDate.Value);
        if (endDate.HasValue) query = query.Where(i => i.Date <= endDate.Value);

        var total = await query.SumAsync(i => i.Amount);
        return Ok(new { total });
    }
}
