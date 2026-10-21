using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

/// <summary>
/// A person or business the user invoices. Kept separate from <see cref="User"/>
/// because these are the user's own clients, not Akanti accounts.
/// </summary>
public class Customer
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? Phone { get; set; }

    /// <summary>PayPal's own id for this contact, when it returns one.</summary>
    [MaxLength(64)]
    public string? PayPalCustomerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
