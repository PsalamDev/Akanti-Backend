using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

public class EmailVerification
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    [Required, MaxLength(10)]
    public string Code { get; set; } = string.Empty;
    
    public DateTime ExpiresAt { get; set; }
    
    public bool IsUsed { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
