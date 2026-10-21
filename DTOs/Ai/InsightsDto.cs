namespace Akanti.API.DTOs.Ai;

/// <summary>
/// The generated insights for a user's current financial period.
/// </summary>
public class InsightsDto
{
    /// <summary>
    /// One-paragraph overview of the period.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    public List<InsightItemDto> Highlights { get; set; } = new List<InsightItemDto>();

    public List<InsightItemDto> Warnings { get; set; } = new List<InsightItemDto>();

    public List<InsightItemDto> Recommendations { get; set; } = new List<InsightItemDto>();

    /// <summary>
    /// Human label for the period covered, e.g. "October 2026".
    /// </summary>
    public string PeriodLabel { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// "ai" when the commentary came from the model, "rules" when it came from the
    /// deterministic fallback.
    /// </summary>
    public string Source { get; set; } = "rules";

    /// <summary>
    /// Set when something worth telling the user happened — e.g. the AI provider was
    /// not configured, so the rule-based fallback was used instead.
    /// </summary>
    public string? Notice { get; set; }

    /// <summary>
    /// The aggregate figures the insights were derived from. Returned so the UI can
    /// show the numbers it is talking about.
    /// </summary>
    public FinancialSnapshotDto? Snapshot { get; set; }
}

public class InsightItemDto
{
    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    /// <summary>
    /// The concrete next step, when there is one.
    /// </summary>
    public string? Action { get; set; }

    /// <summary>
    /// "good", "info", "warning" or "critical".
    /// </summary>
    public string? Severity { get; set; }
}
