using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Ai;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

/// <summary>
/// Computes the aggregate financial picture for a user, and the deterministic
/// findings derived from it.
///
/// This service is the single source of truth for what data may be shown to the AI
/// provider. The snapshot it returns deliberately excludes transaction titles,
/// descriptions, counterparty details, individual amounts and dates — only totals
/// and trends. The assistant is built from this one snapshot, so it cannot drift
/// apart from what the app itself knows.
/// </summary>
public class FinancialAnalysisService : IFinancialAnalysisService
{
    /// <summary>
    /// A single ledger line, reduced to the three fields the analysis needs. Carrying
    /// a named record rather than an anonymous type lets the rows be passed between
    /// methods.
    /// </summary>
    private sealed record LedgerRow(decimal Amount, int? CategoryId, DateTime Date);

    private readonly ApplicationDbContext _context;

    public FinancialAnalysisService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<FinancialSnapshotDto> BuildSnapshotAsync(int userId, DateTime? referenceMonth = null)
    {
        var now = DateTime.UtcNow;
        var anchor = referenceMonth ?? now;

        var monthStart = new DateTime(anchor.Year, anchor.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var prevStart = monthStart.AddMonths(-1);
        var prevEnd = monthStart.AddDays(-1);

        // Budgets can run for a year, so look back far enough to cover the longest
        // period a budget may declare.
        var lookback = monthStart.AddMonths(-24);

        var categoryNames = await _context.Categories
            .Where(c => c.IsDefault || c.UserId == userId)
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var expenseRows = await _context.Expenses
            .Where(e => e.UserId == userId && !e.IsDeleted && e.Date >= lookback && e.Date <= monthEnd)
            .AsNoTracking()
            .Select(e => new LedgerRow(e.Amount, e.CategoryId, e.Date))
            .ToListAsync();

        var incomeRows = await _context.Incomes
            .Where(i => i.UserId == userId && !i.IsDeleted && i.Date >= lookback && i.Date <= monthEnd)
            .AsNoTracking()
            .Select(i => new LedgerRow(i.Amount, i.CategoryId, i.Date))
            .ToListAsync();

        var income = incomeRows
            .Where(i => i.Date >= monthStart && i.Date <= monthEnd)
            .Sum(i => i.Amount);

        var expenses = expenseRows
            .Where(e => e.Date >= monthStart && e.Date <= monthEnd)
            .Sum(e => e.Amount);

        var prevIncome = incomeRows
            .Where(i => i.Date >= prevStart && i.Date <= prevEnd)
            .Sum(i => i.Amount);

        var prevExpenses = expenseRows
            .Where(e => e.Date >= prevStart && e.Date <= prevEnd)
            .Sum(e => e.Amount);

        var totalIncome = await _context.Incomes
            .Where(i => i.UserId == userId && !i.IsDeleted)
            .AsNoTracking()
            .SumAsync(i => i.Amount);

        var totalExpenses = await _context.Expenses
            .Where(e => e.UserId == userId && !e.IsDeleted)
            .AsNoTracking()
            .SumAsync(e => e.Amount);

        var snapshot = new FinancialSnapshotDto
        {
            PeriodLabel = monthStart.ToString("MMMM yyyy"),
            PeriodStart = monthStart.ToString("yyyy-MM-dd"),
            PeriodEnd = monthEnd.ToString("yyyy-MM-dd"),
            Income = income,
            Expenses = expenses,
            Net = income - expenses,
            SavingsRate = income > 0 ? Math.Round((income - expenses) / income * 100, 1) : 0,
            PreviousIncome = prevIncome,
            PreviousExpenses = prevExpenses,
            PreviousNet = prevIncome - prevExpenses,
            PreviousSavingsRate = prevIncome > 0 ? Math.Round((prevIncome - prevExpenses) / prevIncome * 100, 1) : 0,
            TotalBalance = totalIncome - totalExpenses,
            TransactionCount = incomeRows.Count(i => i.Date >= monthStart && i.Date <= monthEnd)
                             + expenseRows.Count(e => e.Date >= monthStart && e.Date <= monthEnd)
        };

        snapshot.ExpenseByCategory = BuildCategoryTotals(
            expenseRows.Where(e => e.Date >= monthStart && e.Date <= monthEnd),
            categoryNames,
            expenses);

        snapshot.IncomeByCategory = BuildCategoryTotals(
            incomeRows.Where(i => i.Date >= monthStart && i.Date <= monthEnd),
            categoryNames,
            income);

        snapshot.Budgets = await BuildBudgetsAsync(userId, expenseRows);

        snapshot.Debts = await BuildDebtsAsync(userId, now);

        snapshot.MonthsWithData = incomeRows.Select(i => (i.Date.Year, i.Date.Month))
            .Concat(expenseRows.Select(e => (e.Date.Year, e.Date.Month)))
            .Distinct()
            .Count();

        return snapshot;
    }

    private static List<CategoryTotalDto> BuildCategoryTotals(
        IEnumerable<LedgerRow> rows,
        Dictionary<int, string> categoryNames,
        decimal total)
    {
        return rows
            .GroupBy(r => r.CategoryId)
            .Select(g => new CategoryTotalDto
            {
                Name = g.Key.HasValue && categoryNames.TryGetValue(g.Key.Value, out var name) ? name : "Uncategorised",
                Amount = g.Sum(x => x.Amount),
                SharePercent = total > 0 ? Math.Round(g.Sum(x => x.Amount) / total * 100, 1) : 0
            })
            .OrderByDescending(c => c.Amount)
            .ToList();
    }

    private async Task<List<BudgetStatusDto>> BuildBudgetsAsync(int userId, List<LedgerRow> expenseRows)
    {
        var budgets = await _context.Budgets
            .Where(b => b.UserId == userId && b.IsActive)
            .AsNoTracking()
            .ToListAsync();

        var results = new List<BudgetStatusDto>();

        foreach (var budget in budgets)
        {
            var actual = ActualSpendForBudget(expenseRows, budget);
            var used = actual ?? budget.Spent;

            results.Add(new BudgetStatusDto
            {
                Name = budget.Name,
                Limit = budget.Amount,
                Spent = budget.Spent,
                ActualSpent = actual,
                PercentageUsed = budget.Amount > 0 ? Math.Round(used / budget.Amount * 100, 1) : 0,
                AlertThresholdPercentage = budget.AlertThresholdPercentage
            });
        }

        return results;
    }

    /// <summary>
    /// Sums the expenses recorded against a budget's category over the budget's own
    /// period. Returns null when the budget is not tied to a category, because there
    /// is then no basis for attributing spending to it.
    /// </summary>
    private static decimal? ActualSpendForBudget(List<LedgerRow> expenseRows, Budget budget)
    {
        if (budget.CategoryId == null)
        {
            return null;
        }

        return expenseRows
            .Where(e => e.CategoryId == budget.CategoryId &&
                        e.Date >= budget.StartDate &&
                        e.Date <= budget.EndDate)
            .Sum(e => e.Amount);
    }

    private async Task<List<DebtSummaryDto>> BuildDebtsAsync(int userId, DateTime now)
    {
        var debts = await _context.Debts
            .Where(d => d.UserId == userId && !d.IsDeleted)
            .AsNoTracking()
            .Select(d => new { d.Type, d.Amount, d.AmountPaid, d.Status, d.DueDate })
            .ToListAsync();

        var results = new List<DebtSummaryDto>();

        foreach (var group in debts.GroupBy(d => d.Type))
        {
            var overdue = group
                .Where(d => d.Status == DebtStatus.Overdue ||
                            (d.DueDate.HasValue && d.DueDate.Value < now && d.Status != DebtStatus.Paid))
                .ToList();

            results.Add(new DebtSummaryDto
            {
                Type = group.Key.ToString(),
                Total = group.Sum(d => d.Amount),
                Paid = group.Sum(d => d.AmountPaid),
                Outstanding = group.Sum(d => d.Amount - d.AmountPaid),
                OverdueCount = overdue.Count,
                OverdueAmount = overdue.Sum(d => d.Amount - d.AmountPaid)
            });
        }

        return results;
    }

    // -----------------------------------------------------------------------
    // Findings
    // -----------------------------------------------------------------------

    public FindingsDto BuildFindings(FinancialSnapshotDto snapshot)
    {
        var findings = new FindingsDto();

        if (snapshot.TransactionCount == 0 && snapshot.MonthsWithData == 0)
        {
            findings.Summary = $"No transactions recorded yet, so there is nothing to analyse for {snapshot.PeriodLabel}.";
            findings.Recommendations.Add(new InsightItemDto
            {
                Title = "Add your first income and expense",
                Detail = "Once Akanti has a few transactions, it can spot patterns in your spending and suggest where to cut back.",
                Action = "Go to Income and Expenses to record your first transactions.",
                Severity = "info"
            });
            return findings;
        }

        findings.Summary = BuildSummary(snapshot);

        // --- Highlights ---------------------------------------------------
        if (snapshot.Income > 0)
        {
            var band = DescribeSavingsBand(snapshot.SavingsRate);
            findings.Highlights.Add(new InsightItemDto
            {
                Title = $"Savings rate is {snapshot.SavingsRate:0.#}%",
                Detail = band,
                Action = snapshot.SavingsRate < 20
                    ? "Try to move spending out of your largest category to reach a 20% savings rate."
                    : null,
                Severity = snapshot.SavingsRate >= 20 ? "good" : "info"
            });
        }

        var topCategory = snapshot.ExpenseByCategory.FirstOrDefault();
        if (topCategory != null && topCategory.Amount > 0)
        {
            findings.Highlights.Add(new InsightItemDto
            {
                Title = $"Biggest spending area is {topCategory.Name}",
                Detail = $"{topCategory.Name} accounts for {topCategory.SharePercent:0.#}% of this month's spending.",
                Action = snapshot.ExpenseByCategory.Count == 1
                    ? "Nearly everything you spend goes to one category — worth checking."
                    : null,
                Severity = "info"
            });
        }

        if (snapshot.PreviousExpenses > 0)
        {
            var change = (snapshot.Expenses - snapshot.PreviousExpenses) / snapshot.PreviousExpenses * 100;
            if (Math.Abs(change) >= 5)
            {
                findings.Highlights.Add(new InsightItemDto
                {
                    Title = change > 0
                        ? $"Spending is up {change:0.#}% on last month"
                        : $"Spending is down {Math.Abs(change):0.#}% on last month",
                    Detail = change > 0
                        ? $"You spent {snapshot.Expenses:0.##} this month against {snapshot.PreviousExpenses:0.##} last month."
                        : $"You spent {snapshot.Expenses:0.##} this month against {snapshot.PreviousExpenses:0.##} last month.",
                    Severity = change > 0 ? "info" : "good"
                });
            }
        }

        var healthyBudgets = snapshot.Budgets
            .Where(b => b.Limit > 0 && b.PercentageUsed < b.AlertThresholdPercentage)
            .ToList();

        if (healthyBudgets.Count > 0)
        {
            findings.Highlights.Add(new InsightItemDto
            {
                Title = $"{healthyBudgets.Count} budget{(healthyBudgets.Count == 1 ? "" : "s")} on track",
                Detail = string.Join(", ", healthyBudgets.Select(b => $"{b.Name} at {b.PercentageUsed:0.#}%")),
                Severity = "good"
            });
        }

        // --- Warnings -----------------------------------------------------
        if (snapshot.Net < 0)
        {
            findings.Warnings.Add(new InsightItemDto
            {
                Title = "You spent more than you earned this month",
                Detail = $"Spending exceeded income by {Math.Abs(snapshot.Net):0.##}.",
                Action = "Review your largest categories for one-off purchases.",
                Severity = "critical"
            });
        }
        else if (snapshot.Income > 0 && snapshot.SavingsRate < 10)
        {
            findings.Warnings.Add(new InsightItemDto
            {
                Title = "Savings rate is below 10%",
                Detail = $"You kept {snapshot.SavingsRate:0.#}% of your income this month.",
                Action = "Even a small rise in your savings rate builds a buffer.",
                Severity = "warning"
            });
        }

        foreach (var budget in snapshot.Budgets.Where(b => b.Limit > 0 && b.PercentageUsed >= b.AlertThresholdPercentage))
        {
            var exceeded = budget.PercentageUsed >= 100;
            findings.Warnings.Add(new InsightItemDto
            {
                Title = exceeded
                    ? $"Budget '{budget.Name}' is over its limit"
                    : $"Budget '{budget.Name}' is close to its limit",
                Detail = $"{budget.PercentageUsed:0.#}% of {budget.Limit:0.##} used.",
                Action = exceeded
                    ? "Decide whether to raise the limit or cut back in this category."
                    : "Check this category before the period ends.",
                Severity = exceeded ? "critical" : "warning"
            });
        }

        foreach (var debt in snapshot.Debts.Where(d => d.OverdueCount > 0))
        {
            findings.Warnings.Add(new InsightItemDto
            {
                Title = $"{debt.OverdueCount} overdue {(debt.Type == "Borrowed" ? "payment" : "receivable")}",
                Detail = $"{debt.OverdueAmount:0.##} is past its due date.",
                Action = "Settle or renegotiate the overdue items first — they usually carry the highest cost.",
                Severity = "critical"
            });
        }

        if (snapshot.PreviousExpenses > 0)
        {
            var change = (snapshot.Expenses - snapshot.PreviousExpenses) / snapshot.PreviousExpenses * 100;
            if (change >= 25)
            {
                findings.Warnings.Add(new InsightItemDto
                {
                    Title = $"Spending jumped {change:0.#}% on last month",
                    Detail = $"{snapshot.PreviousExpenses:0.##} last month against {snapshot.Expenses:0.##} this month.",
                    Action = "Check whether this is a one-off or a new recurring cost.",
                    Severity = "warning"
                });
            }
        }

        // --- Recommendations ----------------------------------------------
        if (topCategory != null && topCategory.SharePercent >= 40 && snapshot.ExpenseByCategory.Count > 1)
        {
            findings.Recommendations.Add(new InsightItemDto
            {
                Title = $"Set a budget for {topCategory.Name}",
                Detail = $"It takes {topCategory.SharePercent:0.#}% of your spending, so a limit here has the most effect.",
                Action = "Create a monthly budget for that category.",
                Severity = "info"
            });
        }

        if (snapshot.Net > 0 && snapshot.SavingsRate < 20)
        {
            findings.Recommendations.Add(new InsightItemDto
            {
                Title = "Aim for a 20% savings rate",
                Detail = $"You saved {snapshot.SavingsRate:0.#}% this month. Saving {snapshot.Income * 0.2m:0.##} would reach 20%.",
                Action = "Set up a standing transfer for the difference.",
                Severity = "info"
            });
        }

        if (snapshot.Budgets.Count == 0 && snapshot.Expenses > 0)
        {
            findings.Recommendations.Add(new InsightItemDto
            {
                Title = "Add budgets to catch overspending early",
                Detail = "Without a budget, overspending only becomes visible after the month is over.",
                Action = "Create a budget for your largest spending category.",
                Severity = "info"
            });
        }

        var borrowed = snapshot.Debts.FirstOrDefault(d => d.Type == "Borrowed" && d.Outstanding > 0);
        if (borrowed != null && snapshot.Net > 0)
        {
            findings.Recommendations.Add(new InsightItemDto
            {
                Title = "Consider clearing debt with this month's surplus",
                Detail = $"You owe {borrowed.Outstanding:0.##} and had {snapshot.Net:0.##} left over this month.",
                Action = "Pay down the highest-cost debt first.",
                Severity = "info"
            });
        }

        if (findings.Warnings.Count == 0 && findings.Highlights.Count > 0)
        {
            findings.Highlights.Add(new InsightItemDto
            {
                Title = "Nothing needs urgent attention",
                Detail = "No budgets are close to their limits and no debts are overdue.",
                Severity = "good"
            });
        }

        return findings;
    }

    /// <summary>
    /// Renders the snapshot as compact text. It carries totals and trends only — no
    /// transaction titles, descriptions, counterparties, individual amounts or dates.
    /// This is the only representation of a user's finances that leaves the server.
    /// </summary>
    public static string RenderSnapshot(FinancialSnapshotDto s)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Period: {s.PeriodLabel} ({s.PeriodStart} to {s.PeriodEnd})");
        sb.AppendLine($"Income: {Num(s.Income)}");
        sb.AppendLine($"Expenses: {Num(s.Expenses)}");
        sb.AppendLine($"Net: {Num(s.Net)}");
        sb.AppendLine($"Savings rate: {Num(s.SavingsRate)}%");
        sb.AppendLine($"Previous month: income {Num(s.PreviousIncome)}, expenses {Num(s.PreviousExpenses)}, net {Num(s.PreviousNet)}, savings rate {Num(s.PreviousSavingsRate)}%");
        sb.AppendLine($"Total balance (all time): {Num(s.TotalBalance)}");
        sb.AppendLine($"Transactions this period: {s.TransactionCount}");
        sb.AppendLine($"Months of history: {s.MonthsWithData}");

        if (s.ExpenseByCategory.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Expenses by category:");
            foreach (var c in s.ExpenseByCategory)
            {
                sb.AppendLine($"- {c.Name}: {Num(c.Amount)} ({Num(c.SharePercent)}%)");
            }
        }

        if (s.IncomeByCategory.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Income by category:");
            foreach (var c in s.IncomeByCategory)
            {
                sb.AppendLine($"- {c.Name}: {Num(c.Amount)} ({Num(c.SharePercent)}%)");
            }
        }

        if (s.Budgets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Budgets:");
            foreach (var b in s.Budgets)
            {
                var actual = b.ActualSpent.HasValue
                    ? $"spent {Num(b.ActualSpent.Value)}"
                    : $"spent {Num(b.Spent)}";
                sb.AppendLine($"- {b.Name}: limit {Num(b.Limit)}, {actual}, {Num(b.PercentageUsed)}% used, alert at {Num(b.AlertThresholdPercentage)}%");
            }
        }

        if (s.Debts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Debts:");
            foreach (var d in s.Debts)
            {
                sb.AppendLine($"- {d.Type}: total {Num(d.Total)}, paid {Num(d.Paid)}, outstanding {Num(d.Outstanding)}, {d.OverdueCount} overdue ({Num(d.OverdueAmount)})");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string Num(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string BuildSummary(FinancialSnapshotDto snapshot)
    {
        if (snapshot.Income == 0 && snapshot.Expenses == 0)
        {
            return $"No income or spending recorded for {snapshot.PeriodLabel}.";
        }

        var summary = $"In {snapshot.PeriodLabel} you recorded {snapshot.Income:0.##} in income and {snapshot.Expenses:0.##} in spending, a net of {snapshot.Net:0.##}";

        if (snapshot.Income > 0)
        {
            summary += $" ({snapshot.SavingsRate:0.#}% saved)";
        }

        if (snapshot.PreviousExpenses > 0)
        {
            var change = (snapshot.Expenses - snapshot.PreviousExpenses) / snapshot.PreviousExpenses * 100;
            if (Math.Abs(change) >= 5)
            {
                summary += change > 0
                    ? $". Spending is {change:0.#}% higher than last month"
                    : $". Spending is {Math.Abs(change):0.#}% lower than last month";
            }
        }

        summary += ".";
        return summary;
    }

    private static string DescribeSavingsBand(decimal rate) => rate switch
    {
        >= 30 => "That is a strong rate — you are building a real buffer.",
        >= 20 => "That is a healthy rate and covers the usual recommended target.",
        >= 10 => "That is positive, but there is room to build a bigger cushion.",
        > 0 => "That is a small margin — most of your income is being spent.",
        _ => "You are currently spending everything you earn."
    };
}
