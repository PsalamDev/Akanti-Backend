using Akanti.API.DTOs.Report;

namespace Akanti.API.Interfaces;

public interface IReportService
{
    Task<ReportDto> GetReportAsync(int userId, DateTime? startDate, DateTime? endDate);
    Task<ProfitLossDto> GetProfitLossAsync(int userId, DateTime? startDate, DateTime? endDate);
}
