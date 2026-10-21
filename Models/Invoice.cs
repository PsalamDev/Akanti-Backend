using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Akanti.API.Models;

/// <summary>
/// Local mirror of a PayPal invoice. PayPal is the system of record for the invoice
/// itself (rendering, delivery, reminders, payment); this row exists so invoices can be
/// listed, filtered and linked to bookkeeping without calling PayPal on every read.
/// </summary>
public class Invoice
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>User-visible invoice number, unique per user.</summary>
    [Required, MaxLength(64)]
    public string Number { get; set; } = string.Empty;

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    [Required, MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Tax { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    public DateTime IssueDate { get; set; }

    public DateTime DueDate { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Set once the invoice has been created in PayPal.</summary>
    [MaxLength(64)]
    public string? PayPalInvoiceId { get; set; }

    /// <summary>Raw status string reported by PayPal, for diagnostics.</summary>
    [MaxLength(40)]
    public string? PayPalStatus { get; set; }

    /// <summary>PayPal-hosted page the customer pays on.</summary>
    [MaxLength(500)]
    public string? RecipientViewUrl { get; set; }

    /// <summary>Set only from a verified PayPal webhook.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>The income record created when this invoice was paid.</summary>
    public int? IncomeId { get; set; }
    public Income? Income { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}

public enum InvoiceStatus
{
    /// <summary>Exists locally only; not yet created in PayPal.</summary>
    Draft,

    /// <summary>Created in PayPal and awaiting payment.</summary>
    Sent,

    /// <summary>Payment confirmed by a verified webhook.</summary>
    Paid,

    Cancelled,

    Refunded
}
