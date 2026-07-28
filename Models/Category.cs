using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

public class Category
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    [MaxLength(50)]
    public string? Icon { get; set; }
    
    [MaxLength(7)]
    public string? Color { get; set; }
    
    public CategoryType Type { get; set; }
    
    public bool IsDefault { get; set; }
    
    public int? UserId { get; set; }
    public User? User { get; set; }
    
    public ICollection<Income> Incomes { get; set; } = new List<Income>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}

public enum CategoryType
{
    Income,
    Expense
}
