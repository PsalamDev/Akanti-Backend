namespace Akanti.API.DTOs.Ai;

/// <summary>
/// The aggregate view of a user's finances for one month, compared with the month
/// before it.
///
/// This is the only thing ever sent to the AI provider. It deliberately contains no
/// transaction titles, descriptions, counterparty details, individual amounts or
/// dates — only totals and trends.
/// </summary>
public class FinancialSnapshotDto
{
    public string PeriodLabel { get; set; } = string.Empty;

    /// <summary>First day of the period, inclusive (yyyy-MM-dd).</summary>
    public string PeriodStart { get; set; } = string.Empty;

    /// <summary>Last day of the period, inclusive (yyyy-MM-dd).</summary>
    public string PeriodEnd { get; set; } = string.Empty;

    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net { get; set; }

    /// <summary>Net as a percentage of income. Negative means spending exceeded income.</summary>
    public decimal SavingsRate { get; set; }

    public decimal PreviousIncome { get; set; }
    public decimal PreviousExpenses { get; set; }
    public decimal PreviousNet { get; set; }
    public decimal PreviousSavingsRate { get; set; }

    /// <summary>All-time income minus all-time expenses.</summary>
    public decimal TotalBalance { get; set; }

    /// <summary>Number of transactions recorded in the period.</summary>
    public int TransactionCount { get; set; }

    /// <summary>How many months the user has any history in.</summary>
    public int MonthsWithData { get; set; }

    public List<CategoryTotalDto> ExpenseByCategory { get; set; } = new List<CategoryTotalDto>();
    public List<CategoryTotalDto> IncomeByCategory { get; set; } = new List<CategoryTotalDto>();
    public List<BudgetStatusDto> Budgets { get; set; } = new List<BudgetStatusDto>();
    public List<DebtSummaryDto> Debts { get; set; } = new List<DebtSummaryDto>();
}

public class CategoryTotalDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    /// <summary>Share of the period's total, as a percentage.</summary>
    public decimal SharePercent { get; set; }
}

public class BudgetStatusDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Limit { get; set; }

    /// <summary>
    /// The spend recorded on the budget itself, which the app stores and the user
    /// maintains by hand.
    /// </summary>
    public decimal Spent { get; set; }

    /// <summary>
    /// The spend actually recorded against this budget's category over the budget's
    /// own period. Null when the budget is not tied to a category, because there is
    /// then nothing to attribute it to.
    /// </summary>
    public decimal? ActualSpent { get; set; }

    /// <summary>
    /// Percentage of the limit used, preferring the actual figure when there is one.
    /// </summary>
    public decimal PercentageUsed { get; set; }

    public decimal AlertThresholdPercentage { get; set; }
}

public class DebtSummaryDto
{
    /// <summary>"Borrowed" (the user owes) or "Lent" (the user is owed).</summary>
    public string Type { get; set; } = string.Empty;

    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Outstanding { get; set; }
    public int OverdueCount { get; set; }
    public decimal OverdueAmount { get; set; }
}
