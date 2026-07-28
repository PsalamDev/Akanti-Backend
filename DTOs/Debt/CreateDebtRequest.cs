using System.ComponentModel.DataAnnotations;

namespace Akanti.API.DTOs.Debt;

public class CreateDebtRequest
{
    [Required, MaxLength(200)]
    public string Description { get; set; } = string.Empty;
    
    [Required]
    public decimal Amount { get; set; }
    
    [Required]
    public string Type { get; set; } = "Borrowed";
    
    [MaxLength(200)]
    public string? PersonName { get; set; }

    [MaxLength(200)]
    public string? PersonEmail { get; set; }
    
    public DateTime? DueDate { get; set; }
    
    public bool HasReminder { get; set; }
    
    public int ReminderDaysBefore { get; set; } = 3;
}

public class DebtDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal RemainingAmount => Amount - AmountPaid;
    public string Type { get; set; } = string.Empty;
    public string? PersonName { get; set; }
    public string? PersonEmail { get; set; }
    public DateTime? DueDate { get; set; }
    public bool HasReminder { get; set; }
    public int ReminderDaysBefore { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateTime.UtcNow && Status == "Pending";
}

public class UpdateDebtPaymentRequest
{
    [Required]
    public decimal Amount { get; set; }
}
