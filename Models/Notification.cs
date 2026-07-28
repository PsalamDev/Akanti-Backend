using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

public class Notification
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    [Required, MaxLength(500)]
    public string Message { get; set; } = string.Empty;
    
    public NotificationType Type { get; set; }
    
    public bool IsRead { get; set; }
    
    public string? ActionUrl { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum NotificationType
{
    BudgetAlert,
    DebtReminder,
    MonthlySummary,
    SystemAlert,
    AIRecommendation
}
