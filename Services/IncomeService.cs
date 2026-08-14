using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Income;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class IncomeService : IIncomeService
{
    private readonly ApplicationDbContext _context;

    public IncomeService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<IncomeDto>> GetAllAsync(int userId, DateTime? startDate, DateTime? endDate)
    {
        var query = _context.Incomes
            .Where(i => i.UserId == userId && !i.IsDeleted)
            .Include(i => i.Category)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(i => i.Date >= startDate.Value);
        if (endDate.HasValue) query = query.Where(i => i.Date <= endDate.Value);

        var incomes = await query.OrderByDescending(i => i.Date).ToListAsync();

        return incomes.Select(i => new IncomeDto
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
        }).ToList();
    }

    public async Task<IncomeDto?> GetByIdAsync(int userId, int id)
    {
        var income = await _context.Incomes
            .Include(i => i.Category)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId && !i.IsDeleted);

        if (income == null) return null;

        return new IncomeDto
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
        };
    }

    public async Task<IncomeDto> CreateAsync(int userId, CreateIncomeRequest request)
    {
        var income = new Income
        {
            UserId = userId,
            Title = request.Title,
            Description = request.Description,
            Amount = request.Amount,
            CategoryId = request.CategoryId,
            Date = request.Date,
            Frequency = Enum.TryParse<IncomeFrequency>(request.Frequency, true, out var freq) ? freq : IncomeFrequency.OneTime
        };

        _context.Incomes.Add(income);
        await _context.SaveChangesAsync();

        return new IncomeDto
        {
            Id = income.Id,
            Title = income.Title,
            Description = income.Description,
            Amount = income.Amount,
            CategoryId = income.CategoryId,
            Date = income.Date,
            Frequency = income.Frequency.ToString(),
            CreatedAt = income.CreatedAt
        };
    }

    public async Task<bool> UpdateAsync(int userId, int id, CreateIncomeRequest request)
    {
        var income = await _context.Incomes
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId && !i.IsDeleted);

        if (income == null) return false;

        income.Title = request.Title;
        income.Description = request.Description;
        income.Amount = request.Amount;
        income.CategoryId = request.CategoryId;
        income.Date = request.Date;
        income.Frequency = Enum.TryParse<IncomeFrequency>(request.Frequency, true, out var freq) ? freq : IncomeFrequency.OneTime;
        income.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var income = await _context.Incomes
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId && !i.IsDeleted);

        if (income == null) return false;

        income.IsDeleted = true;
        income.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<decimal> GetTotalAsync(int userId, DateTime? startDate, DateTime? endDate)
    {
        var query = _context.Incomes
            .Where(i => i.UserId == userId && !i.IsDeleted)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(i => i.Date >= startDate.Value);
        if (endDate.HasValue) query = query.Where(i => i.Date <= endDate.Value);

        return await query.SumAsync(i => i.Amount);
    }
}
