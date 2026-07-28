using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

public class AuditLog
{
    public int Id { get; set; }
    
    public int? UserId { get; set; }
    public User? User { get; set; }
    
    [Required, MaxLength(50)]
    public string Action { get; set; } = string.Empty;
    
    [Required, MaxLength(100)]
    public string EntityName { get; set; } = string.Empty;
    
    public int? EntityId { get; set; }
    
    public string? OldValues { get; set; }
    
    public string? NewValues { get; set; }
    
    [MaxLength(50)]
    public string? IpAddress { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
