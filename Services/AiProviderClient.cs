using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Akanti.API.Interfaces;

namespace Akanti.API.Services;

/// <summary>
/// Talks to a language model over HTTP. The provider is chosen by configuration, so
/// switching between them is an environment change rather than a code change.
///
/// Supported providers:
///   gemini      Google AI Studio, native structured output
///   openai      api.openai.com, native structured output
///   groq        api.groq.com, OpenAI-compatible
///   openrouter  openrouter.ai, OpenAI-compatible
///   anthropic   api.anthropic.com, JSON requested through the prompt
/// </summary>
public class AiProviderClient : IAiProviderClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiProviderClient> _logger;

    /// <summary>
    /// The model that last worked. Held here rather than in configuration because
    /// providers retire model names without notice, and a retired name should not
    /// take the feature down until the next deploy.
    /// </summary>
    private string? _activeModel;

    public AiProviderClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AiProviderClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public string Provider => (_configuration["Ai:Provider"] ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// The model in use. Once a model has been proven to work it is remembered for the
    /// lifetime of the process, so a model name the provider has retired is corrected
    /// without needing a redeploy.
    /// </summary>
    public string Model => _activeModel ?? ConfiguredModel;

    private string ConfiguredModel
    {
        get
        {
            var configured = (_configuration["Ai:Model"] ?? "").Trim();
            if (configured.Length > 0) return configured;

            return Provider switch
            {
                "gemini" => "gemini-3.8-flash",
                "anthropic" => "claude-sonnet-4-5",
                "groq" => "llama-3.3-70b-versatile",
                "openrouter" => "google/gemini-3.8-flash",
                _ => "gpt-4o-mini"
            };
        }
    }

    public bool IsEnabled => _configuration.GetValue("Ai:Enabled", true);

    public bool IsConfigured
    {
        get
        {
            if (!IsEnabled) return false;
            if (Provider.Length == 0) return false;
            if (!KnownProviders.Contains(Provider)) return false;

            var apiKey = (_configuration["Ai:ApiKey"] ?? "").Trim();
            return apiKey.Length > 0;
        }
    }

    private static readonly HashSet<string> KnownProviders =
        new(StringComparer.OrdinalIgnoreCase) { "gemini", "openai", "groq", "openrouter", "anthropic" };

    public async Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        object? jsonSchema,
        CancellationToken cancellationToken = default)
    {
        return await CompleteJsonAsync(
            systemPrompt,
            new[] { new ChatTurn("user", userPrompt) },
            jsonSchema,
            cancellationToken);
    }

    public async Task<string> CompleteJsonAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> turns,
        object? jsonSchema,
        CancellationToken cancellationToken = default)
    {
        var text = await SendAsync(systemPrompt, turns, jsonSchema, cancellationToken);
        return ExtractJson(text);
    }

    public async Task<string> CompleteTextAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(systemPrompt, turns, jsonSchema: null, cancellationToken);
    }

    private async Task<string> SendAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> turns,
        object? jsonSchema,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new AiProviderException(
                $"AI provider is not configured. Set Ai:Provider and Ai:ApiKey (current provider: '{Provider}').");
        }

        var candidates = CandidateModels();
        AiProviderException? last = null;

        for (var i = 0; i < candidates.Count; i++)
        {
            var model = candidates[i];
            try
            {
                var text = await SendOnceAsync(systemPrompt, turns, jsonSchema, model, cancellationToken);

                // Remember what worked, so the next request goes straight to it.
                if (!string.Equals(model, _activeModel, StringComparison.OrdinalIgnoreCase))
                {
                    _activeModel = model;
                    _logger.LogInformation("AI provider {Provider} is using model {Model}.", Provider, model);
                }

                return text;
            }
            catch (AiProviderException ex) when (
                i < candidates.Count - 1 && IsModelUnavailable(ex.StatusCode, ex.Message))
            {
                _logger.LogWarning(
                    "AI provider {Provider} does not offer model {Model}. Trying {Next} instead.",
                    Provider, model, candidates[i + 1]);
                last = ex;
            }
        }

        throw last ?? new AiProviderException("The AI provider could not be reached.");
    }

    /// <summary>
    /// The models to try, in order. The configured model comes first, then a short list
    /// of known-good alternatives. Providers retire model names without warning — Gemini
    /// withdrew <c>gemini-2.5-flash</c> from new accounts — so the feature keeps
    /// working instead of quietly falling back to the rule-based insights.
    /// </summary>
    private IReadOnlyList<string> CandidateModels()
    {
        var fallbacks = Provider switch
        {
            "gemini" => new[] { "gemini-3.8-flash", "gemini-3.5-flash", "gemini-2.5-flash" },
            "anthropic" => new[] { "claude-sonnet-4-5", "claude-3-5-sonnet-latest" },
            "groq" => new[] { "llama-3.3-70b-versatile", "llama-3.1-8b-instant" },
            "openrouter" => new[] { "google/gemini-3.8-flash", "google/gemini-2.5-flash" },
            _ => new[] { "gpt-4o-mini" }
        };

        var list = new List<string> { Model };
        foreach (var fallback in fallbacks)
        {
            if (!list.Contains(fallback, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(fallback);
            }
        }
        return list;
    }

    /// <summary>
    /// True when the provider's complaint is about the model rather than the request.
    /// A retired model is worth retrying with a different name; a bad key is not.
    /// </summary>
    private static bool IsModelUnavailable(int? statusCode, string message)
    {
        if (statusCode != 404 && statusCode != 400) return false;

        var lower = message.ToLowerInvariant();
        return lower.Contains("no longer available")
            || lower.Contains("unsupported model")
            || lower.Contains("unknown model")
            || lower.Contains("invalid model")
            || lower.Contains("model not found")
            || lower.Contains("models/") && lower.Contains("not found");
    }

    private async Task<string> SendOnceAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> turns,
        object? jsonSchema,
        string model,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("AiApi");
        var apiKey = (_configuration["Ai:ApiKey"] ?? "").Trim();

        var (url, request, extract) = Provider switch
        {
            "gemini" => BuildGeminiRequest(systemPrompt, turns, jsonSchema, model),
            "openai" or "groq" or "openrouter" => BuildOpenAiRequest(systemPrompt, turns, jsonSchema, model),
            "anthropic" => BuildAnthropicRequest(systemPrompt, turns, model),
            _ => throw new AiProviderException($"Unsupported AI provider '{Provider}'.")
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Content = new StringContent(request, Encoding.UTF8, "application/json");

        switch (Provider)
        {
            case "gemini":
                message.Headers.Add("x-goog-api-key", apiKey);
                break;
            case "anthropic":
                message.Headers.Add("x-api-key", apiKey);
                message.Headers.Add("anthropic-version", "2023-06-01");
                break;
            default:
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                break;
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("The AI provider did not respond within the request timeout.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException($"Could not reach the AI provider: {ex.Message}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Surface the provider's own reason, trimmed, so a bad key or a bad model
            // name is diagnosable without extra logging.
            var detail = body.Length > 600 ? body[..600] : body;
            _logger.LogError("AI provider {Provider} returned {Status}: {Body}", Provider, (int)response.StatusCode, detail);
            throw new AiProviderException(
                $"{Provider} rejected the request ({(int)response.StatusCode}). {detail}",
                (int)response.StatusCode);
        }

        var text = ExtractText(body, extract);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiProviderException($"{Provider} returned an empty response.");
        }

        return text;
    }

    // -----------------------------------------------------------------------
    // Request builders
    // -----------------------------------------------------------------------

    private (string Url, string Body, Func<JsonDocument, string?> Extract) BuildGeminiRequest(
        string systemPrompt, IReadOnlyList<ChatTurn> turns, object? jsonSchema, string model)
    {
        var baseUrl = ResolveBaseUrl("https://generativelanguage.googleapis.com/v1beta");
        var url = $"{baseUrl}/models/{model}:generateContent";

        var generationConfig = new Dictionary<string, object?>
        {
            ["temperature"] = 0.4
        };

        if (jsonSchema != null)
        {
            generationConfig["responseMimeType"] = "application/json";
            generationConfig["responseSchema"] = jsonSchema;
        }

        var contents = turns
            .Select(t => new Dictionary<string, object?>
            {
                ["role"] = t.Role == "assistant" ? "model" : "user",
                ["parts"] = new[] { new Dictionary<string, object?> { ["text"] = t.Content } }
            })
            .ToArray();

        var payload = new Dictionary<string, object?>
        {
            ["systemInstruction"] = new Dictionary<string, object?>
            {
                ["parts"] = new[] { new Dictionary<string, object?> { ["text"] = systemPrompt } }
            },
            ["contents"] = contents,
            ["generationConfig"] = generationConfig
        };

        string? Extract(JsonDocument doc)
        {
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates)) return null;
            if (candidates.GetArrayLength() == 0) return null;

            var parts = candidates[0].GetProperty("content").GetProperty("parts");
            var sb = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var t))
                {
                    sb.Append(t.GetString());
                }
            }
            return sb.ToString();
        }

        return (url, JsonSerializer.Serialize(payload), Extract);
    }

    private (string Url, string Body, Func<JsonDocument, string?> Extract) BuildOpenAiRequest(
        string systemPrompt, IReadOnlyList<ChatTurn> turns, object? jsonSchema, string model)
    {
        var baseUrl = ResolveBaseUrl(Provider switch
        {
            "groq" => "https://api.groq.com/openai/v1",
            "openrouter" => "https://openrouter.ai/api/v1",
            _ => "https://api.openai.com/v1"
        });

        var messages = new List<Dictionary<string, object?>>
        {
            new() { ["role"] = "system", ["content"] = systemPrompt }
        };

        foreach (var turn in turns)
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = turn.Role == "assistant" ? "assistant" : "user",
                ["content"] = turn.Content
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = 0.4,
            ["messages"] = messages
        };

        if (jsonSchema != null)
        {
            payload["response_format"] = new Dictionary<string, object?> { ["type"] = "json_object" };
        }

        string? Extract(JsonDocument doc)
        {
            if (!doc.RootElement.TryGetProperty("choices", out var choices)) return null;
            if (choices.GetArrayLength() == 0) return null;
            return choices[0].GetProperty("message").GetProperty("content").GetString();
        }

        return ($"{baseUrl}/chat/completions", JsonSerializer.Serialize(payload), Extract);
    }

    private (string Url, string Body, Func<JsonDocument, string?> Extract) BuildAnthropicRequest(
        string systemPrompt, IReadOnlyList<ChatTurn> turns, string model)
    {
        var baseUrl = ResolveBaseUrl("https://api.anthropic.com/v1");

        var messages = turns
            .Select(t => new Dictionary<string, object?>
            {
                ["role"] = t.Role == "assistant" ? "assistant" : "user",
                ["content"] = t.Content
            })
            .ToArray();

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = 2000,
            ["temperature"] = 0.4,
            ["system"] = systemPrompt,
            ["messages"] = messages
        };

        string? Extract(JsonDocument doc)
        {
            if (!doc.RootElement.TryGetProperty("content", out var content)) return null;
            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("text", out var t))
                {
                    sb.Append(t.GetString());
                }
            }
            return sb.ToString();
        }

        return ($"{baseUrl}/messages", JsonSerializer.Serialize(payload), Extract);
    }

    private string ResolveBaseUrl(string fallback)
    {
        var configured = (_configuration["Ai:BaseUrl"] ?? "").Trim().TrimEnd('/');
        return configured.Length > 0 ? configured : fallback;
    }

    // -----------------------------------------------------------------------
    // Response handling
    // -----------------------------------------------------------------------

    private static string ExtractText(string body, Func<JsonDocument, string?> extract)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return extract(doc) ?? string.Empty;
        }
        catch (JsonException)
        {
            // Not JSON — return the raw body so the caller reports something useful.
            return body;
        }
    }

    /// <summary>
    /// Pulls the JSON object out of a model reply. Models sometimes wrap it in a
    /// markdown fence or add a sentence of preamble, so the text is located rather
    /// than assumed to be the whole reply.
    /// </summary>
    internal static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closing > 0)
            {
                trimmed = trimmed[..closing];
            }
        }

        trimmed = trimmed.Trim();

        // Fast path: the whole reply is the object.
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        throw new AiProviderException("The AI provider did not return JSON.");
    }
}
