namespace Akanti.API.Interfaces;

/// <summary>
/// One turn in a conversation with the model.
/// </summary>
/// <param name="Role">"user" or "assistant".</param>
/// <param name="Content">The text of the turn.</param>
public record ChatTurn(string Role, string Content);

/// <summary>
/// A thin, provider-agnostic client for talking to a language model.
/// </summary>
public interface IAiProviderClient
{
    /// <summary>True when the feature is switched on by configuration.</summary>
    bool IsEnabled { get; }

    /// <summary>True when a provider and API key are both configured.</summary>
    bool IsConfigured { get; }

    /// <summary>"gemini", "openai", "groq", "openrouter", "anthropic" or "none".</summary>
    string Provider { get; }

    /// <summary>The model that will be used.</summary>
    string Model { get; }

    /// <summary>
    /// Sends a prompt and returns the model's reply as JSON.
    /// </summary>
    /// <param name="systemPrompt">Instructions that frame the task.</param>
    /// <param name="userPrompt">The content to work with.</param>
    /// <param name="jsonSchema">
    /// An optional JSON Schema describing the required shape. Providers that support
    /// native structured output receive it; the rest are asked for JSON in the prompt
    /// and their reply is parsed leniently.
    /// </param>
    Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, object? jsonSchema, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a conversation and returns the model's reply as plain text.
    /// </summary>
    Task<string> CompleteTextAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns, CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised when the provider rejects a request or returns something unusable. The
/// message carries the provider's own reason, so the failure can be diagnosed
/// without turning on debug logging.
/// </summary>
public class AiProviderException : Exception
{
    public int? StatusCode { get; }

    public AiProviderException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
