using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Akanti.API.Models;

public class AIRecommendation
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    [Required]
    public string Title { get; set; } = string.Empty;
    
    [Required]
    public string Content { get; set; } = string.Empty;
    
    public AIRecommendationType Type { get; set; }
    
    public decimal? FinancialHealthScore { get; set; }
    
    public bool IsRead { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum AIRecommendationType
{
    SpendingAnalysis,
    BudgetRecommendation,
    ExpensePrediction,
    SavingsSuggestion,
    FinancialHealthScore
}
