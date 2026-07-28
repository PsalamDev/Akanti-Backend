using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Dashboard;
using Akanti.API.Models;
using Akanti.API.Services;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IAIService _aiService;

    public DashboardController(ApplicationDbContext context, IAIService aiService)
    {
        _context = context;
        _aiService = aiService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> GetDashboard()
    {
        var userId = GetUserId();
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var monthlyIncome = await _context.Incomes
            .Where(i => i.UserId == userId && i.Date >= monthStart && i.Date <= monthEnd && !i.IsDeleted)
            .SumAsync(i => i.Amount);

        var monthlyExpenses = await _context.Expenses
            .Where(e => e.UserId == userId && e.Date >= monthStart && e.Date <= monthEnd && !e.IsDeleted)
            .SumAsync(e => e.Amount);

        var totalIncome = await _context.Incomes
            .Where(i => i.UserId == userId && !i.IsDeleted)
            .SumAsync(i => i.Amount);

        var totalExpenses = await _context.Expenses
            .Where(e => e.UserId == userId && !e.IsDeleted)
            .SumAsync(e => e.Amount);

        var recentTransactions = new List<RecentTransactionDto>();

        var recentIncomes = await _context.Incomes
            .Where(i => i.UserId == userId && !i.IsDeleted)
            .Include(i => i.Category)
            .OrderByDescending(i => i.Date)
            .Take(5)
            .Select(i => new RecentTransactionDto
            {
                Id = i.Id,
                Description = i.Title,
                Amount = i.Amount,
                Type = "Income",
                Date = i.Date,
                CategoryName = i.Category != null ? i.Category.Name : null
            })
            .ToListAsync();

        var recentExpenses = await _context.Expenses
            .Where(e => e.UserId == userId && !e.IsDeleted)
            .Include(e => e.Category)
            .OrderByDescending(e => e.Date)
            .Take(5)
            .Select(e => new RecentTransactionDto
            {
                Id = e.Id,
                Description = e.Title,
                Amount = e.Amount,
                Type = "Expense",
                Date = e.Date,
                CategoryName = e.Category != null ? e.Category.Name : null
            })
            .ToListAsync();

        recentTransactions = recentIncomes.Concat(recentExpenses)
            .OrderByDescending(t => t.Date)
            .Take(10)
            .ToList();

        var budgets = await _context.Budgets
            .Where(b => b.UserId == userId && b.IsActive)
            .Select(b => new BudgetOverviewDto
            {
                Name = b.Name,
                Amount = b.Amount,
                Spent = b.Spent,
                PercentageUsed = b.Amount > 0 ? (double)(b.Spent / b.Amount) * 100 : 0
            })
            .ToListAsync();

        var upcomingDebts = await _context.Debts
            .Where(d => d.UserId == userId && !d.IsDeleted && d.DueDate.HasValue && d.Status != DebtStatus.Paid)
            .OrderBy(d => d.DueDate)
            .Take(5)
            .Select(d => new UpcomingDebtDto
            {
                Description = d.Description,
                Amount = d.Amount,
                RemainingAmount = d.Amount - d.AmountPaid,
                DueDate = d.DueDate,
                Type = d.Type.ToString()
            })
            .ToListAsync();

        decimal? healthScore = null;
        try { healthScore = await _aiService.GetFinancialHealthScoreAsync(userId); } catch { }

        return Ok(new DashboardDto
        {
            TotalBalance = totalIncome - totalExpenses,
            MonthlyIncome = monthlyIncome,
            MonthlyExpenses = monthlyExpenses,
            SavingsRate = monthlyIncome > 0 ? ((monthlyIncome - monthlyExpenses) / monthlyIncome) * 100 : 0,
            RecentTransactions = recentTransactions,
            Budgets = budgets,
            UpcomingDebts = upcomingDebts,
            FinancialHealthScore = healthScore
        });
    }
}