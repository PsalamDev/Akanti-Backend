namespace Akanti.API.DTOs.Ai;

/// <summary>
/// The findings the app computes from a user's own records.
///
/// They are the factual baseline the model is given, and the answer the assistant
/// falls back to when no provider is configured or the model cannot be trusted.
/// </summary>
public class FindingsDto
{
    /// <summary>One-line read on the period.</summary>
    public string Summary { get; set; } = string.Empty;

    public List<InsightItemDto> Highlights { get; set; } = new List<InsightItemDto>();

    public List<InsightItemDto> Warnings { get; set; } = new List<InsightItemDto>();

    public List<InsightItemDto> Recommendations { get; set; } = new List<InsightItemDto>();
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
