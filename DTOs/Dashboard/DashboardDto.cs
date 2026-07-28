namespace Akanti.API.DTOs.Dashboard;

public class DashboardDto
{
    public decimal TotalBalance { get; set; }
    public decimal MonthlyIncome { get; set; }
    public decimal MonthlyExpenses { get; set; }
    public decimal SavingsRate { get; set; }
    public List<RecentTransactionDto> RecentTransactions { get; set; } = new();
    public List<BudgetOverviewDto> Budgets { get; set; } = new();
    public List<UpcomingDebtDto> UpcomingDebts { get; set; } = new();
    public decimal? FinancialHealthScore { get; set; }
}

public class RecentTransactionDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? CategoryName { get; set; }
}

public class BudgetOverviewDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Spent { get; set; }
    public double PercentageUsed { get; set; }
}

public class UpcomingDebtDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public string Type { get; set; } = string.Empty;
}
