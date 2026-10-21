using System.Globalization;
using System.Text.RegularExpressions;
using Akanti.API.DTOs.Ai;

namespace Akanti.API.Services;

/// <summary>
/// Checks an answer against the figures the app actually holds, so a number the
/// model invented never reaches the user.
///
/// The model is told to use only the figures in the snapshot, but a model that is
/// asked to talk about money will occasionally produce one that is not there. An
/// invented amount is worse than no answer at all, because the user cannot tell.
/// </summary>
public static class AiFigureGuard
{
    /// <summary>
    /// Amounts below this are counts, percentages and years rather than money, so
    /// they are not checked. A rounded percentage is not a meaningful error.
    /// </summary>
    private const decimal MinCheckedAmount = 1000m;

    private static readonly Regex NumberPattern = new(@"(?<![\w.])\d[\d,]*(?:\.\d+)?(?![\w])", RegexOptions.Compiled);

    /// <summary>
    /// Returns the monetary figures in the text that the snapshot does not support.
    /// An empty list means every amount in the text can be traced back to the
    /// user's own records.
    /// </summary>
    public static List<string> FindUnsupportedFigures(string? text, FinancialSnapshotDto snapshot)
    {
        var allowed = CollectAllowedAmounts(snapshot);
        var unsupported = new List<string>();

        foreach (Match match in NumberPattern.Matches(text ?? string.Empty))
        {
            var raw = match.Value.Replace(",", string.Empty);

            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            if (value < MinCheckedAmount)
            {
                continue;
            }

            // A conversational answer rounds more freely than a structured one, so
            // the tolerance is wider than an exact match.
            var supported = allowed.Any(a => Math.Abs(a - value) <= Math.Max(1m, a * 0.05m));
            if (!supported)
            {
                unsupported.Add(raw);
            }
        }

        return unsupported.Distinct().ToList();
    }

    /// <summary>
    /// Every amount the user actually holds, plus the handful of figures that can
    /// be derived from them and are therefore fair to mention.
    /// </summary>
    private static List<decimal> CollectAllowedAmounts(FinancialSnapshotDto s)
    {
        var allowed = new List<decimal>
        {
            s.Income, s.Expenses, s.Net,
            s.PreviousIncome, s.PreviousExpenses, s.PreviousNet,
            s.TotalBalance
        };

        allowed.AddRange(s.ExpenseByCategory.Select(c => c.Amount));
        allowed.AddRange(s.IncomeByCategory.Select(c => c.Amount));

        foreach (var b in s.Budgets)
        {
            allowed.Add(b.Limit);
            allowed.Add(b.Spent);
            if (b.ActualSpent.HasValue) allowed.Add(b.ActualSpent.Value);
        }

        foreach (var d in s.Debts)
        {
            allowed.Add(d.Total);
            allowed.Add(d.Paid);
            allowed.Add(d.Outstanding);
            allowed.Add(d.OverdueAmount);
        }

        // A change between two periods is a fair thing to talk about even though it
        // is not itself a stored total.
        allowed.Add(Math.Abs(s.Expenses - s.PreviousExpenses));
        allowed.Add(Math.Abs(s.Income - s.PreviousIncome));
        allowed.Add(Math.Abs(s.Net - s.PreviousNet));

        // The 20% savings target the findings suggest.
        if (s.Income > 0)
        {
            allowed.Add(s.Income * 0.2m);
        }

        return allowed;
    }
}
