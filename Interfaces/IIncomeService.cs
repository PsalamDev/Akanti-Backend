using Akanti.API.DTOs.Income;

namespace Akanti.API.Interfaces;

public interface IIncomeService
{
    Task<List<IncomeDto>> GetAllAsync(int userId, DateTime? startDate, DateTime? endDate);
    Task<IncomeDto?> GetByIdAsync(int userId, int id);
    Task<IncomeDto> CreateAsync(int userId, CreateIncomeRequest request);
    Task<bool> UpdateAsync(int userId, int id, CreateIncomeRequest request);
    Task<bool> DeleteAsync(int userId, int id);
    Task<decimal> GetTotalAsync(int userId, DateTime? startDate, DateTime? endDate);
}
