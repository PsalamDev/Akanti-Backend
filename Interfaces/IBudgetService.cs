using Akanti.API.DTOs.Budget;

namespace Akanti.API.Interfaces;

public interface IBudgetService
{
    Task<List<BudgetDto>> GetAllAsync(int userId);
    Task<BudgetDto?> GetByIdAsync(int userId, int id);
    Task<BudgetDto> CreateAsync(int userId, CreateBudgetRequest request);
    Task<bool> UpdateAsync(int userId, int id, CreateBudgetRequest request);
    Task<bool> DeleteAsync(int userId, int id);
    Task<List<BudgetDto>> GetAlertsAsync(int userId);
}
