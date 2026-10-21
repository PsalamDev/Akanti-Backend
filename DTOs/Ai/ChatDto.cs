using System.ComponentModel.DataAnnotations;

namespace Akanti.API.DTOs.Ai;

public class ChatSessionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public int MessageCount { get; set; }
}

public class ChatMessageDto
{
    public int Id { get; set; }

    /// <summary>"user" or "assistant".</summary>
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>True when the assistant answered from the rule-based fallback.</summary>
    public bool UsedFallback { get; set; }
}

public class SendChatMessageRequest
{
    [Required]
    [MaxLength(4000)]
    public string Message { get; set; } = string.Empty;
}

public class SendChatMessageResponse
{
    public ChatMessageDto UserMessage { get; set; } = new ChatMessageDto();
    public ChatMessageDto Reply { get; set; } = new ChatMessageDto();
}

/// <summary>
/// Whether the AI features are usable, and how. Never contains a secret.
/// </summary>
public class AiStatusDto
{
    /// <summary>True when the feature is switched on by configuration.</summary>
    public bool Enabled { get; set; }

    /// <summary>True when a provider and API key are both configured.</summary>
    public bool Configured { get; set; }

    /// <summary>"gemini", "openai", "groq", "openrouter", "anthropic" or "none".</summary>
    public string Provider { get; set; } = "none";

    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// "ai" when the model writes the commentary, "rules" when the deterministic
    /// fallback does.
    /// </summary>
    public string Mode { get; set; } = "rules";
}
