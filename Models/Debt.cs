using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Akanti.API.Models;

public class Debt
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    [Required, MaxLength(200)]
    public string Description { get; set; } = string.Empty;
    
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountPaid { get; set; }
    
    public DebtType Type { get; set; }
    
    [MaxLength(200)]
    public string? PersonName { get; set; }

    [MaxLength(200)]
    public string? PersonEmail { get; set; }
    
    public DateTime? DueDate { get; set; }
    
    public bool HasReminder { get; set; }
    
    public int ReminderDaysBefore { get; set; } = 3;
    
    public DebtStatus Status { get; set; } = DebtStatus.Pending;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    public DateTime? LastRemindedAt { get; set; }
    
    public bool IsDeleted { get; set; }
    
    [NotMapped]
    public decimal RemainingAmount => Amount - AmountPaid;
    
    [NotMapped]
    public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateTime.UtcNow && Status == DebtStatus.Pending;
}

public enum DebtType
{
    Borrowed,
    Lent
}

public enum DebtStatus
{
    Pending,
    PartiallyPaid,
    Paid,
    Overdue
}
