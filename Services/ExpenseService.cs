using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Expense;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class ExpenseService : IExpenseService
{
    private readonly ApplicationDbContext _context;

    public ExpenseService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ExpenseDto>> GetAllAsync(int userId, DateTime? startDate, DateTime? endDate, int? categoryId)
    {
        var query = _context.Expenses
            .Where(e => e.UserId == userId && !e.IsDeleted)
            .Include(e => e.Category)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(e => e.Date >= startDate.Value);
        if (endDate.HasValue) query = query.Where(e => e.Date <= endDate.Value);
        if (categoryId.HasValue) query = query.Where(e => e.CategoryId == categoryId.Value);

        var expenses = await query.OrderByDescending(e => e.Date).ToListAsync();

        return expenses.Select(e => new ExpenseDto
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
        }).ToList();
    }

    public async Task<ExpenseDto?> GetByIdAsync(int userId, int id)
    {
        var expense = await _context.Expenses
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && !e.IsDeleted);

        if (expense == null) return null;

        return new ExpenseDto
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
        };
    }

    public async Task<ExpenseDto> CreateAsync(int userId, CreateExpenseRequest request)
    {
        var expense = new Expense
        {
            UserId = userId,
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

        return new ExpenseDto
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
        };
    }

    public async Task<bool> UpdateAsync(int userId, int id, CreateExpenseRequest request)
    {
        var expense = await _context.Expenses
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && !e.IsDeleted);

        if (expense == null) return false;

        expense.Title = request.Title;
        expense.Description = request.Description;
        expense.Amount = request.Amount;
        expense.CategoryId = request.CategoryId;
        expense.Date = request.Date;
        expense.Frequency = Enum.TryParse<ExpenseFrequency>(request.Frequency, true, out var freq) ? freq : ExpenseFrequency.OneTime;
        expense.ReceiptUrl = request.ReceiptUrl;
        expense.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var expense = await _context.Expenses
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && !e.IsDeleted);

        if (expense == null) return false;

        expense.IsDeleted = true;
        expense.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<decimal> GetTotalAsync(int userId, DateTime? startDate, DateTime? endDate)
    {
        var query = _context.Expenses
            .Where(e => e.UserId == userId && !e.IsDeleted)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(e => e.Date >= startDate.Value);
        if (endDate.HasValue) query = query.Where(e => e.Date <= endDate.Value);

        return await query.SumAsync(e => e.Amount);
    }
}
