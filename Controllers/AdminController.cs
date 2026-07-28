using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Admin;
using Akanti.API.Models;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AdminController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardDto>> GetDashboard()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var totalUsers = await _context.Users.CountAsync();
        var activeUsers = await _context.Users.CountAsync(u => u.IsActive);
        var newUsersThisMonth = await _context.Users.CountAsync(u => u.CreatedAt >= monthStart);

        var totalIncome = await _context.Incomes.Where(i => !i.IsDeleted).SumAsync(i => i.Amount);
        var totalExpenses = await _context.Expenses.Where(e => !e.IsDeleted).SumAsync(e => e.Amount);
        var totalIncomes = await _context.Incomes.CountAsync(i => !i.IsDeleted);
        var totalExpensesCount = await _context.Expenses.CountAsync(e => !e.IsDeleted);
        var totalBudgets = await _context.Budgets.CountAsync();
        var totalDebts = await _context.Debts.CountAsync(d => !d.IsDeleted);

        return Ok(new AdminDashboardDto
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            NewUsersThisMonth = newUsersThisMonth,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            TotalIncomes = totalIncomes,
            TotalExpensesCount = totalExpensesCount,
            TotalBudgets = totalBudgets,
            TotalDebts = totalDebts
        });
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<AdminUserDto>>> GetUsers([FromQuery] string? search)
    {
        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(term) || u.Email.ToLower().Contains(term));
        }

        var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();

        var result = new List<AdminUserDto>();
        foreach (var u in users)
        {
            result.Add(new AdminUserDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                UserType = u.UserType.ToString(),
                IsActive = u.IsActive,
                IsAdmin = u.IsAdmin,
                CreatedAt = u.CreatedAt,
                TotalIncome = await _context.Incomes.Where(i => i.UserId == u.Id && !i.IsDeleted).SumAsync(i => i.Amount),
                TotalExpenses = await _context.Expenses.Where(e => e.UserId == u.Id && !e.IsDeleted).SumAsync(e => e.Amount),
                IncomeCount = await _context.Incomes.CountAsync(i => i.UserId == u.Id && !i.IsDeleted),
                ExpenseCount = await _context.Expenses.CountAsync(e => e.UserId == u.Id && !e.IsDeleted),
                BudgetCount = await _context.Budgets.CountAsync(b => b.UserId == u.Id),
                DebtCount = await _context.Debts.CountAsync(d => d.UserId == u.Id && !d.IsDeleted)
            });
        }

        return Ok(result);
    }

    [HttpGet("users/{id}")]
    public async Task<ActionResult<AdminUserDetailDto>> GetUserDetail(int id)
    {
        var user = await _context.Users.FindAsync(id)
            ?? throw new KeyNotFoundException("User not found.");

        var totalIncome = await _context.Incomes.Where(i => i.UserId == id && !i.IsDeleted).SumAsync(i => i.Amount);
        var totalExpenses = await _context.Expenses.Where(e => e.UserId == id && !e.IsDeleted).SumAsync(e => e.Amount);

        var incomes = await _context.Incomes
            .Where(i => i.UserId == id && !i.IsDeleted)
            .Include(i => i.Category)
            .OrderByDescending(i => i.Date)
            .Select(i => new AdminIncomeDto
            {
                Id = i.Id,
                Title = i.Title,
                Amount = i.Amount,
                CategoryName = i.Category != null ? i.Category.Name : "Uncategorized",
                Date = i.Date,
                Frequency = i.Frequency.ToString()
            })
            .ToListAsync();

        var expenses = await _context.Expenses
            .Where(e => e.UserId == id && !e.IsDeleted)
            .Include(e => e.Category)
            .OrderByDescending(e => e.Date)
            .Select(e => new AdminExpenseDto
            {
                Id = e.Id,
                Title = e.Title,
                Amount = e.Amount,
                CategoryName = e.Category != null ? e.Category.Name : "Uncategorized",
                Date = e.Date,
                Frequency = e.Frequency.ToString()
            })
            .ToListAsync();

        var budgets = await _context.Budgets
            .Where(b => b.UserId == id)
            .Select(b => new AdminBudgetDto
            {
                Id = b.Id,
                Name = b.Name,
                Amount = b.Amount,
                Spent = b.Spent,
                Period = b.Period.ToString(),
                IsActive = b.IsActive
            })
            .ToListAsync();

        var debts = await _context.Debts
            .Where(d => d.UserId == id && !d.IsDeleted)
            .Select(d => new AdminDebtDto
            {
                Id = d.Id,
                Description = d.Description,
                Amount = d.Amount,
                AmountPaid = d.AmountPaid,
                Type = d.Type.ToString(),
                Status = d.Status.ToString(),
                DueDate = d.DueDate
            })
            .ToListAsync();

        return Ok(new AdminUserDetailDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            UserType = user.UserType.ToString(),
            IsActive = user.IsActive,
            IsAdmin = user.IsAdmin,
            CreatedAt = user.CreatedAt,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            SavingsRate = totalIncome > 0 ? ((totalIncome - totalExpenses) / totalIncome) * 100 : 0,
            Incomes = incomes,
            Expenses = expenses,
            Budgets = budgets,
            Debts = debts
        });
    }

    [HttpPut("users/{id}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await _context.Users.FindAsync(id)
            ?? throw new KeyNotFoundException("User not found.");

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { user.Id, user.IsActive });
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<List<AdminAuditLogDto>>> GetAuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var logs = await _context.AuditLogs
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminAuditLogDto
            {
                Id = a.Id,
                UserEmail = a.User != null ? a.User.Email : null,
                Action = a.Action,
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                IpAddress = a.IpAddress,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Ok(logs);
    }
}
