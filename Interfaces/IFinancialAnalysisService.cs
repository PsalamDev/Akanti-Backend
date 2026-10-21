using Akanti.API.DTOs.Ai;

namespace Akanti.API.Interfaces;

public interface IFinancialAnalysisService
{
    /// <summary>
    /// Builds the aggregate view of the user's finances for the current month,
    /// compared with the month before.
    /// </summary>
    Task<FinancialSnapshotDto> BuildSnapshotAsync(int userId, DateTime? referenceMonth = null);

    /// <summary>
    /// Produces the deterministic insights from a snapshot. These are the fallback
    /// when no AI provider is configured, and the factual baseline the model is asked
    /// to interpret.
    /// </summary>
    InsightsDto BuildRuleBasedInsights(FinancialSnapshotDto snapshot);
}
