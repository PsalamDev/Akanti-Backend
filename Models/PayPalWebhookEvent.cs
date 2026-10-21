using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

/// <summary>
/// One row per PayPal webhook delivery. The unique <see cref="PayPalEventId"/> is the
/// idempotency key: PayPal retries failed deliveries, and a replay must never create a
/// second payment record.
/// </summary>
public class PayPalWebhookEvent
{
    public int Id { get; set; }

    /// <summary>PayPal's event id, unique across deliveries. The idempotency key.</summary>
    [Required, MaxLength(64)]
    public string PayPalEventId { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? ResourceType { get; set; }

    /// <summary>Full JSON body, kept so an event can be replayed or inspected.</summary>
    public string RawPayload { get; set; } = string.Empty;

    /// <summary>Result of PayPal signature verification. Events that fail are stored but never acted on.</summary>
    public bool SignatureVerified { get; set; }

    /// <summary>Null until the event has been handled.</summary>
    public DateTime? ProcessedAt { get; set; }

    [MaxLength(1000)]
    public string? ProcessingError { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
