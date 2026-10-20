using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class CategoryService : ICategoryService
{
    private readonly ApplicationDbContext _context;

    public CategoryService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Category>> GetAllAsync(string? type)
    {
        var query = _context.Categories
            .Where(c => c.IsDefault || c.UserId == null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<CategoryType>(type, true, out var categoryType))
            query = query.Where(c => c.Type == categoryType);

        return await query.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<Category?> GetByIdAsync(int id)
    {
        return await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    }
}
