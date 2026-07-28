namespace Akanti.API.DTOs.Report;

public class ReportDto
{
    public string Title { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetProfitLoss => TotalIncome - TotalExpenses;
    public List<CategoryBreakdownDto> IncomeBreakdown { get; set; } = new();
    public List<CategoryBreakdownDto> ExpenseBreakdown { get; set; } = new();
}

public class CategoryBreakdownDto
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
}
