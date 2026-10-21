using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

/// <summary>
/// State for an AI-assisted invoice conversation. The transcript and the latest model
/// proposal live here so nothing is created in PayPal until the user approves, and so an
/// approved invoice can be traced back to the conversation that produced it.
/// </summary>
public class InvoiceDraft
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public InvoiceDraftStatus Status { get; set; } = InvoiceDraftStatus.Collecting;

    /// <summary>Conversation transcript as a JSON array of {role, content} turns.</summary>
    public string MessagesJson { get; set; } = "[]";

    /// <summary>Latest structured invoice proposal returned by the model, or null.</summary>
    public string? ProposedInvoiceJson { get; set; }

    /// <summary>Set when the draft is approved into a real invoice.</summary>
    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public enum InvoiceDraftStatus
{
    /// <summary>Still gathering details from the user.</summary>
    Collecting,

    /// <summary>Model produced a complete proposal awaiting approval.</summary>
    Ready,

    /// <summary>Approved and turned into an <see cref="Invoice"/>.</summary>
    Approved,

    Discarded
}
