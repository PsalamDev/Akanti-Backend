using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Admin;
using Akanti.API.Interfaces;

namespace Akanti.API.Services;

public class AdminService : IAdminService
{
    private readonly ApplicationDbContext _context;

    public AdminService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync()
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

        return new AdminDashboardDto
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
        };
    }

    public async Task<PagedResult<AdminUserDto>> GetUsersAsync(string? search, int page, int pageSize)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(term) || u.Email.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync();

        var users = await query
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                UserType = u.UserType.ToString(),
                IsActive = u.IsActive,
                IsAdmin = u.IsAdmin,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<AdminUserDto>
        {
            Items = users,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<AdminUserDetailDto?> GetUserDetailAsync(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return null;

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

        return new AdminUserDetailDto
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
        };
    }

    public async Task<AdminUserTotalsDto?> GetUserTotalsAsync(int id)
    {
        var userExists = await _context.Users.AnyAsync(u => u.Id == id);
        if (!userExists) return null;

        var totalIncome = await _context.Incomes.Where(i => i.UserId == id && !i.IsDeleted).SumAsync(i => i.Amount);
        var incomeCount = await _context.Incomes.CountAsync(i => i.UserId == id && !i.IsDeleted);
        var totalExpenses = await _context.Expenses.Where(e => e.UserId == id && !e.IsDeleted).SumAsync(e => e.Amount);
        var expenseCount = await _context.Expenses.CountAsync(e => e.UserId == id && !e.IsDeleted);

        return new AdminUserTotalsDto
        {
            UserId = id,
            TotalIncome = totalIncome,
            IncomeCount = incomeCount,
            TotalExpenses = totalExpenses,
            ExpenseCount = expenseCount
        };
    }

    public async Task<(int Id, bool IsActive)?> ToggleActiveAsync(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return null;

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return (user.Id, user.IsActive);
    }

    public async Task<List<AdminAuditLogDto>> GetAuditLogsAsync(int page, int pageSize)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 200);

        return await _context.AuditLogs
            .AsNoTracking()
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
    }
}
