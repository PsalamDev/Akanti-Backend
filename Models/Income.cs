using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Akanti.API.Models;

public class Income
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    
    public DateTime Date { get; set; }
    
    public IncomeFrequency Frequency { get; set; } = IncomeFrequency.OneTime;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    public bool IsDeleted { get; set; }
}

public enum IncomeFrequency
{
    OneTime,
    Weekly,
    BiWeekly,
    Monthly,
    Quarterly,
    Yearly
}
