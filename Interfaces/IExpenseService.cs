using Akanti.API.DTOs.Expense;

namespace Akanti.API.Interfaces;

public interface IExpenseService
{
    Task<List<ExpenseDto>> GetAllAsync(int userId, DateTime? startDate, DateTime? endDate, int? categoryId);
    Task<ExpenseDto?> GetByIdAsync(int userId, int id);
    Task<ExpenseDto> CreateAsync(int userId, CreateExpenseRequest request);
    Task<bool> UpdateAsync(int userId, int id, CreateExpenseRequest request);
    Task<bool> DeleteAsync(int userId, int id);
    Task<decimal> GetTotalAsync(int userId, DateTime? startDate, DateTime? endDate);
}
