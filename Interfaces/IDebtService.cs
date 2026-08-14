using Akanti.API.DTOs.Debt;

namespace Akanti.API.Interfaces;

public interface IDebtService
{
    Task<List<DebtDto>> GetAllAsync(int userId, string? type);
    Task<DebtDto?> GetByIdAsync(int userId, int id);
    Task<DebtDto> CreateAsync(int userId, CreateDebtRequest request);
    Task<bool> UpdateAsync(int userId, int id, CreateDebtRequest request);
    Task<bool> DeleteAsync(int userId, int id);
    Task<(int Id, decimal AmountPaid, string Status)?> RecordPaymentAsync(int userId, int id, UpdateDebtPaymentRequest request);
    Task<List<DebtDto>> GetUpcomingAsync(int userId, int days);
    Task SendReminderAsync(int userId, int id);
}
