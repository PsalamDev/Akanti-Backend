using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Akanti.API.DTOs.Income;

public class CreateIncomeRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    [Required]
    public decimal Amount { get; set; }
    
    public int? CategoryId { get; set; }
    
    [Required]
    public DateTime Date { get; set; }
    
    public string Frequency { get; set; } = "OneTime";
}

public class IncomeDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public DateTime Date { get; set; }
    public string Frequency { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
