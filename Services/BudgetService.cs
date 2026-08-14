using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Budget;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class BudgetService : IBudgetService
{
    private readonly ApplicationDbContext _context;

    public BudgetService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<BudgetDto>> GetAllAsync(int userId)
    {
        var budgets = await _context.Budgets
            .Where(b => b.UserId == userId)
            .Include(b => b.Category)
            .ToListAsync();

        return budgets.Select(b => new BudgetDto
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
        }).ToList();
    }

    public async Task<BudgetDto?> GetByIdAsync(int userId, int id)
    {
        var budget = await _context.Budgets
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (budget == null) return null;

        return new BudgetDto
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
        };
    }

    public async Task<BudgetDto> CreateAsync(int userId, CreateBudgetRequest request)
    {
        var budget = new Budget
        {
            UserId = userId,
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

        return new BudgetDto
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
        };
    }

    public async Task<bool> UpdateAsync(int userId, int id, CreateBudgetRequest request)
    {
        var budget = await _context.Budgets
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (budget == null) return false;

        budget.Name = request.Name;
        budget.Amount = request.Amount;
        budget.CategoryId = request.CategoryId;
        budget.Period = Enum.TryParse<BudgetPeriod>(request.Period, true, out var p) ? p : BudgetPeriod.Monthly;
        budget.StartDate = request.StartDate;
        budget.EndDate = request.EndDate;
        budget.AlertThresholdPercentage = request.AlertThresholdPercentage;
        budget.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var budget = await _context.Budgets
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (budget == null) return false;

        _context.Budgets.Remove(budget);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<BudgetDto>> GetAlertsAsync(int userId)
    {
        var budgets = await _context.Budgets
            .Where(b => b.UserId == userId && b.IsActive)
            .ToListAsync();

        return budgets
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
    }
}
