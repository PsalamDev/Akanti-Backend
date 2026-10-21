using System.ComponentModel.DataAnnotations;

namespace Akanti.API.Models;

/// <summary>
/// One conversation in the AI assistant. Messages live in <see cref="AiChatMessage"/>.
/// </summary>
public class AiChatSession
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// Shown in the session list. Defaults to the first question the user asked.
    /// </summary>
    [MaxLength(200)]
    public string Title { get; set; } = "New conversation";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public ICollection<AiChatMessage> Messages { get; set; } = new List<AiChatMessage>();
}
