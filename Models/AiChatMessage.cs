using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

/// <summary>
/// A single turn in an AI conversation.
/// </summary>
public class AiChatMessage
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public AiChatSession Session { get; set; } = null!;

    /// <summary>
    /// "user" or "assistant".
    /// </summary>
    [MaxLength(20)]
    public string Role { get; set; } = "user";

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// True when the assistant could not reach the provider and answered from the
    /// rule-based fallback instead.
    /// </summary>
    public bool UsedFallback { get; set; } = false;
}
