using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Report;
using Akanti.API.Models;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ReportController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<ReportDto>> GetReport([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var start = startDate ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var end = endDate ?? DateTime.UtcNow;

        var incomes = await _context.Incomes
            .Where(i => i.UserId == GetUserId() && i.Date >= start && i.Date <= end && !i.IsDeleted)
            .Include(i => i.Category)
            .ToListAsync();

        var expenses = await _context.Expenses
            .Where(e => e.UserId == GetUserId() && e.Date >= start && e.Date <= end && !e.IsDeleted)
            .Include(e => e.Category)
            .ToListAsync();

        var totalIncome = incomes.Sum(i => i.Amount);
        var totalExpenses = expenses.Sum(e => e.Amount);

        var incomeBreakdown = incomes
            .GroupBy(i => i.Category?.Name ?? "Uncategorized")
            .Select(g => new CategoryBreakdownDto
            {
                CategoryName = g.Key,
                Amount = g.Sum(i => i.Amount),
                Percentage = totalIncome > 0 ? (double)(g.Sum(i => i.Amount) / totalIncome) * 100 : 0
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var expenseBreakdown = expenses
            .GroupBy(e => e.Category?.Name ?? "Uncategorized")
            .Select(g => new CategoryBreakdownDto
            {
                CategoryName = g.Key,
                Amount = g.Sum(e => e.Amount),
                Percentage = totalExpenses > 0 ? (double)(g.Sum(e => e.Amount) / totalExpenses) * 100 : 0
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        return Ok(new ReportDto
        {
            Title = $"Financial Report ({start:MMM dd yyyy} - {end:MMM dd yyyy})",
            StartDate = start,
            EndDate = end,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            IncomeBreakdown = incomeBreakdown,
            ExpenseBreakdown = expenseBreakdown
        });
    }

    [HttpGet("profit-loss")]
    public async Task<ActionResult<object>> GetProfitLoss([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var start = startDate ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var end = endDate ?? DateTime.UtcNow;

        var totalIncome = await _context.Incomes
            .Where(i => i.UserId == GetUserId() && i.Date >= start && i.Date <= end && !i.IsDeleted)
            .SumAsync(i => i.Amount);

        var totalExpenses = await _context.Expenses
            .Where(e => e.UserId == GetUserId() && e.Date >= start && e.Date <= end && !e.IsDeleted)
            .SumAsync(e => e.Amount);

        return Ok(new
        {
            period = new { start, end },
            income = totalIncome,
            expenses = totalExpenses,
            netProfitLoss = totalIncome - totalExpenses,
            savingsRate = totalIncome > 0 ? ((totalIncome - totalExpenses) / totalIncome) * 100 : 0
        });
    }
}