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
    /// Produces the deterministic findings from a snapshot. These are the fallback
    /// when no AI provider is configured, and the factual baseline the model is asked
    /// to work from.
    /// </summary>
    FindingsDto BuildFindings(FinancialSnapshotDto snapshot);
}
