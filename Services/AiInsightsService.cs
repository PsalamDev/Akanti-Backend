using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akanti.API.DTOs.Ai;
using Akanti.API.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Akanti.API.Services;

/// <summary>
/// Produces the insights shown on the Insights page.
///
/// The flow is: build the aggregate snapshot, compute the deterministic findings,
/// then ask the model to interpret them. The deterministic findings are both the
/// fallback when no provider is configured and the factual baseline the model is
/// given, so the model is never the only source of truth.
/// </summary>
public class AiInsightsService : IAiInsightsService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly IFinancialAnalysisService _analysis;
    private readonly IAiProviderClient _provider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiInsightsService> _logger;

    public AiInsightsService(
        IFinancialAnalysisService analysis,
        IAiProviderClient provider,
        IMemoryCache cache,
        ILogger<AiInsightsService> logger)
    {
        _analysis = analysis;
        _provider = provider;
        _cache = cache;
        _logger = logger;
    }

    public AiStatusDto GetStatus() => new AiStatusDto
    {
        Enabled = _provider.IsEnabled,
        Configured = _provider.IsConfigured,
        Provider = _provider.IsConfigured ? _provider.Provider : "none",
        Model = _provider.IsConfigured ? _provider.Model : string.Empty,
        Mode = _provider.IsConfigured ? "ai" : "rules"
    };

    public async Task<InsightsDto> GetInsightsAsync(
        int userId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"ai:insights:{userId}";

        if (!forceRefresh && _cache.TryGetValue(cacheKey, out InsightsDto? cached) && cached != null)
        {
            return cached;
        }

        var snapshot = await _analysis.BuildSnapshotAsync(userId);
        var ruleBased = _analysis.BuildRuleBasedInsights(snapshot);

        if (!_provider.IsConfigured)
        {
            ruleBased.Notice = "AI is not configured, so these insights were calculated directly from your records. "
                            + "Set Ai__ApiKey on the server to add AI commentary.";
            _cache.Set(cacheKey, ruleBased, CacheDuration);
            return ruleBased;
        }

        try
        {
            var generated = await GenerateWithAiAsync(snapshot, ruleBased, cancellationToken);

            if (generated == null)
            {
                ruleBased.Notice = "The AI provider could not be used, so these insights were calculated directly from your records.";
                _cache.Set(cacheKey, ruleBased, CacheDuration);
                return ruleBased;
            }

            generated.Snapshot = snapshot;
            _cache.Set(cacheKey, generated, CacheDuration);
            return generated;
        }
        catch (AiProviderException ex)
        {
            _logger.LogWarning("AI insights fell back to rule-based output: {Message}", ex.Message);
            ruleBased.Notice = $"The AI provider could not be reached ({ex.Message}). These insights were calculated directly from your records.";
            _cache.Set(cacheKey, ruleBased, CacheDuration);
            return ruleBased;
        }
    }

    private async Task<InsightsDto?> GenerateWithAiAsync(
        FinancialSnapshotDto snapshot,
        InsightsDto ruleBased,
        CancellationToken cancellationToken)
    {
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(snapshot, ruleBased);

        var raw = await _provider.CompleteJsonAsync(systemPrompt, userPrompt, BuildSchema(), cancellationToken);

        var parsed = ParseInsights(raw);
        if (parsed == null)
        {
            _logger.LogWarning("AI provider returned JSON that could not be read as insights; using rule-based output.");
            return null;
        }

        // Guard against the model inventing figures. Only amounts large enough to be
        // a monetary value are checked — rounding a percentage is harmless, but
        // inventing an amount is not.
        var unsupported = FindUnsupportedFigures(parsed, snapshot);
        if (unsupported.Count > 0)
        {
            _logger.LogWarning(
                "AI response mentioned figures that are not in the snapshot ({Figures}); using rule-based output.",
                string.Join(", ", unsupported));
            return null;
        }

        parsed.PeriodLabel = snapshot.PeriodLabel;
        parsed.GeneratedAt = DateTime.UtcNow;
        parsed.Source = "ai";
        parsed.Notice = null;

        return parsed;
    }

    // -----------------------------------------------------------------------
    // Prompting
    // -----------------------------------------------------------------------

    private static string BuildSystemPrompt() =>
        "You are the financial analyst inside Akanti, a personal finance app.\n\n" +
        "You are given the user's financial snapshot: aggregate totals for one month and the month " +
        "before it. You must follow these rules:\n\n" +
        "- Use ONLY the figures in the snapshot. Never state a number, percentage or date that is not there.\n" +
        "- If the snapshot does not contain what is needed, say so plainly instead of estimating.\n" +
        "- Write for one person, not an accountant. Be specific and actionable, and avoid filler.\n" +
        "- Amounts carry no currency symbol. Write them as plain numbers, e.g. 45000.\n" +
        "- Keep every field to one or two sentences.\n" +
        "- Do not give investment, tax or legal advice. Stay on budgeting and cash-flow observations.\n" +
        "- Warnings are for genuine problems; do not pad the list. If nothing needs attention, leave it empty.";

    private static string BuildUserPrompt(FinancialSnapshotDto snapshot, InsightsDto ruleBased)
    {
        var sb = new StringBuilder();

        sb.AppendLine("FINANCIAL SNAPSHOT");
        sb.AppendLine(RenderSnapshot(snapshot));

        sb.AppendLine();
        sb.AppendLine("DETERMINISTIC FINDINGS (computed by the app from the snapshot above)");
        foreach (var item in ruleBased.Highlights.Concat(ruleBased.Warnings).Concat(ruleBased.Recommendations))
        {
            sb.AppendLine($"- [{item.Severity}] {item.Title}: {item.Detail}");
        }

        sb.AppendLine();
        sb.AppendLine("Produce the insights JSON now.");

        return sb.ToString();
    }

    /// <summary>
    /// Renders the snapshot as compact text. It carries totals and trends only — no
    /// transaction titles, descriptions, counterparties, individual amounts or dates.
    /// </summary>
    internal static string RenderSnapshot(FinancialSnapshotDto s)
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

    // -----------------------------------------------------------------------
    // Schema and parsing
    // -----------------------------------------------------------------------

    private static Dictionary<string, object?> BuildSchema()
    {
        Dictionary<string, object?> ItemSchema() => new()
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["title"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["detail"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["action"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["severity"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "good", "info", "warning", "critical" }
                }
            },
            ["required"] = new[] { "title", "detail" }
        };

        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["summary"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["highlights"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = ItemSchema() },
                ["warnings"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = ItemSchema() },
                ["recommendations"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = ItemSchema() }
            },
            ["required"] = new[] { "summary", "highlights", "warnings", "recommendations" }
        };
    }

    private static InsightsDto? ParseInsights(string raw)
    {
        InsightsDto? result;

        try
        {
            result = JsonSerializer.Deserialize<InsightsDto>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return null;
        }

        if (result == null || string.IsNullOrWhiteSpace(result.Summary))
        {
            return null;
        }

        result.Highlights = Clean(result.Highlights);
        result.Warnings = Clean(result.Warnings);
        result.Recommendations = Clean(result.Recommendations);

        return result;
    }

    private static List<InsightItemDto> Clean(List<InsightItemDto>? items)
    {
        if (items == null) return new List<InsightItemDto>();

        return items
            .Where(i => i != null && !string.IsNullOrWhiteSpace(i.Title))
            .Select(i =>
            {
                i.Title = i.Title.Trim();
                i.Detail = (i.Detail ?? string.Empty).Trim();
                i.Action = string.IsNullOrWhiteSpace(i.Action) ? null : i.Action.Trim();
                i.Severity = NormaliseSeverity(i.Severity);
                return i;
            })
            .ToList();
    }

    private static string NormaliseSeverity(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "good" => "good",
        "warning" => "warning",
        "critical" => "critical",
        _ => "info"
    };

    // -----------------------------------------------------------------------
    // Hallucination guard
    // -----------------------------------------------------------------------

    private static readonly Regex NumberPattern = new(@"(?<![\w.])\d[\d,]*(?:\.\d+)?(?![\w])", RegexOptions.Compiled);

    /// <summary>
    /// Returns the monetary figures mentioned in the generated text that do not
    /// appear in the snapshot.
    /// </summary>
    private static List<string> FindUnsupportedFigures(InsightsDto insights, FinancialSnapshotDto snapshot)
    {
        var allowed = CollectAllowedAmounts(snapshot);

        var text = new StringBuilder();
        text.AppendLine(insights.Summary);
        foreach (var item in insights.Highlights.Concat(insights.Warnings).Concat(insights.Recommendations))
        {
            text.AppendLine(item.Title);
            text.AppendLine(item.Detail);
            if (item.Action != null) text.AppendLine(item.Action);
        }

        var unsupported = new List<string>();

        foreach (Match match in NumberPattern.Matches(text.ToString()))
        {
            var raw = match.Value.Replace(",", string.Empty);

            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            // Only amounts are checked. Small numbers are counts and percentages, and
            // a rounded percentage is not a meaningful error.
            if (value < 1000)
            {
                continue;
            }

            var matchFound = allowed.Any(a => Math.Abs(a - value) <= Math.Max(1m, a * 0.005m));
            if (!matchFound)
            {
                unsupported.Add(raw);
            }
        }

        return unsupported.Distinct().ToList();
    }

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

        return allowed;
    }
}
