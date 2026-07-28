using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

public class User
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;
    
    [Required, MaxLength(200)]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    public string PasswordHash { get; set; } = string.Empty;
    
    [MaxLength(20)]
    public string? PhoneNumber { get; set; }
    
    public UserType UserType { get; set; }
    
    public string? ProfileImageUrl { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public bool IsAdmin { get; set; } = false;
    
    public bool IsEmailVerified { get; set; } = false;
    
    public ICollection<Income> Incomes { get; set; } = new List<Income>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<Budget> Budgets { get; set; } = new List<Budget>();
    public ICollection<Debt> Debts { get; set; } = new List<Debt>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<AIRecommendation> AIRecommendations { get; set; } = new List<AIRecommendation>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}

public enum UserType
{
    Student,
    Freelancer,
    Entrepreneur,
    SmallBusiness,
    NGO,
    PersonalFinance
}
