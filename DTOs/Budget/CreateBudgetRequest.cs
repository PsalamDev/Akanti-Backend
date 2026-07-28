using System.ComponentModel.DataAnnotations;

namespace Akanti.API.DTOs.Budget;

public class CreateBudgetRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public decimal Amount { get; set; }
    
    public int? CategoryId { get; set; }
    
    [Required]
    public string Period { get; set; } = "Monthly";
    
    [Required]
    public DateTime StartDate { get; set; }
    
    [Required]
    public DateTime EndDate { get; set; }
    
    public decimal AlertThresholdPercentage { get; set; } = 80;
}

public class BudgetDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Spent { get; set; }
    public decimal Remaining => Amount - Spent;
    public double PercentageUsed => Amount > 0 ? (double)(Spent / Amount) * 100 : 0;
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Period { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public decimal AlertThresholdPercentage { get; set; }
}
