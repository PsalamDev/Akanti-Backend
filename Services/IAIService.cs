using Akanti.API.DTOs.Report;

namespace Akanti.API.Services;

public interface IAIService
{
    Task<string> GetSpendingAnalysisAsync(int userId, DateTime startDate, DateTime endDate);
    Task<string> GetBudgetRecommendationAsync(int userId, int budgetId);
    Task<string> GetSavingsSuggestionsAsync(int userId);
    Task<decimal> GetFinancialHealthScoreAsync(int userId);
    Task<string> GetExpensePredictionAsync(int userId);
    Task<string> GetChatResponseAsync(int userId, string message);
}
