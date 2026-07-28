using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Akanti.API.Models;

public class Budget
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal Spent { get; set; }
    
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    
    public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;
    
    public DateTime StartDate { get; set; }
    
    public DateTime EndDate { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public decimal AlertThresholdPercentage { get; set; } = 80;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
}

public enum BudgetPeriod
{
    Weekly,
    Monthly,
    Quarterly,
    Yearly
}
