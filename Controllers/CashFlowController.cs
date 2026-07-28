using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.CashFlow;
using Akanti.API.Models;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CashFlowController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public CashFlowController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<CashFlowDto>> GetCashFlow([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var start = startDate ?? DateTime.UtcNow.AddMonths(-1);
        var end = endDate ?? DateTime.UtcNow;

        var incomes = await _context.Incomes
            .Where(i => i.UserId == GetUserId() && i.Date >= start && i.Date <= end && !i.IsDeleted)
            .ToListAsync();

        var expenses = await _context.Expenses
            .Where(e => e.UserId == GetUserId() && e.Date >= start && e.Date <= end && !e.IsDeleted)
            .ToListAsync();

        var dailyBreakdown = new List<DailyCashFlowDto>();
        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            var dayIncomes = incomes.Where(i => i.Date.Date == date).Sum(i => i.Amount);
            var dayExpenses = expenses.Where(e => e.Date.Date == date).Sum(e => e.Amount);
            dailyBreakdown.Add(new DailyCashFlowDto
            {
                Date = date,
                Income = dayIncomes,
                Expenses = dayExpenses
            });
        }

        return Ok(new CashFlowDto
        {
            TotalIncome = incomes.Sum(i => i.Amount),
            TotalExpenses = expenses.Sum(e => e.Amount),
            PeriodStart = start,
            PeriodEnd = end,
            DailyBreakdown = dailyBreakdown
        });
    }

    [HttpGet("monthly")]
    public async Task<ActionResult<List<CashFlowDto>>> GetMonthlyCashFlow([FromQuery] int months = 12)
    {
        var result = new List<CashFlowDto>();
        var now = DateTime.UtcNow;

        for (int i = 0; i < months; i++)
        {
            var monthStart = new DateTime(now.AddMonths(-i).Year, now.AddMonths(-i).Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var income = await _context.Incomes
                .Where(x => x.UserId == GetUserId() && x.Date >= monthStart && x.Date <= monthEnd && !x.IsDeleted)
                .SumAsync(x => x.Amount);

            var expense = await _context.Expenses
                .Where(x => x.UserId == GetUserId() && x.Date >= monthStart && x.Date <= monthEnd && !x.IsDeleted)
                .SumAsync(x => x.Amount);

            result.Add(new CashFlowDto
            {
                TotalIncome = income,
                TotalExpenses = expense,
                PeriodStart = monthStart,
                PeriodEnd = monthEnd
            });
        }

        return Ok(result);
    }
}