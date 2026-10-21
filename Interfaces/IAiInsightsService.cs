using Akanti.API.DTOs.Ai;

namespace Akanti.API.Interfaces;

public interface IAiInsightsService
{
    /// <summary>
    /// Returns insights for the user's current financial period. Results are cached
    /// briefly, because generating them costs an API call.
    /// </summary>
    Task<InsightsDto> GetInsightsAsync(int userId, bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the AI features are usable, and how. Never contains a secret.
    /// </summary>
    AiStatusDto GetStatus();
}
