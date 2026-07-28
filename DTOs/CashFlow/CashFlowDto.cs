namespace Akanti.API.DTOs.CashFlow;

public class CashFlowDto
{
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetCashFlow => TotalIncome - TotalExpenses;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public List<DailyCashFlowDto> DailyBreakdown { get; set; } = new();
}

public class DailyCashFlowDto
{
    public DateTime Date { get; set; }
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net => Income - Expenses;
}
