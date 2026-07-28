using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.Models;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CategoryController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public CategoryController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<Category>>> GetAll([FromQuery] string? type)
    {
        var query = _context.Categories
            .Where(c => c.IsDefault || c.UserId == null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<CategoryType>(type, true, out var categoryType))
            query = query.Where(c => c.Type == categoryType);

        var categories = await query.OrderBy(c => c.Name).ToListAsync();
        return Ok(categories);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Category>> GetById(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null) return NotFound();
        return Ok(category);
    }
}