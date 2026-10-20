using Akanti.API.Models;

namespace Akanti.API.Interfaces;

public interface ICategoryService
{
    Task<List<Category>> GetAllAsync(string? type);
    Task<Category?> GetByIdAsync(int id);
}
