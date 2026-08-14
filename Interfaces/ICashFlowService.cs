using Akanti.API.DTOs.CashFlow;

namespace Akanti.API.Interfaces;

public interface ICashFlowService
{
    Task<CashFlowDto> GetCashFlowAsync(int userId, DateTime? startDate, DateTime? endDate);
    Task<List<CashFlowDto>> GetMonthlyCashFlowAsync(int userId, int months);
}
