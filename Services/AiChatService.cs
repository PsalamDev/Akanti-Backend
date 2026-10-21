using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Ai;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

/// <summary>
/// The conversational panel.
///
/// Conversations are stored server-side, so they survive a reload and are available
/// on any device. Every turn is grounded in the same aggregate snapshot the
/// insights page uses — the assistant never sees transaction titles, descriptions,
/// counterparties, individual amounts or dates.
/// </summary>
public class AiChatService : IAiChatService
{
    /// <summary>How many past turns are sent to the model. Enough for context, bounded for cost.</summary>
    private const int MaxTurnsSent = 12;

    private readonly ApplicationDbContext _context;
    private readonly IFinancialAnalysisService _analysis;
    private readonly IAiProviderClient _provider;
    private readonly ILogger<AiChatService> _logger;

    public AiChatService(
        ApplicationDbContext context,
        IFinancialAnalysisService analysis,
        IAiProviderClient provider,
        ILogger<AiChatService> logger)
    {
        _context = context;
        _analysis = analysis;
        _provider = provider;
        _logger = logger;
    }

    public async Task<List<ChatSessionDto>> GetSessionsAsync(int userId)
    {
        var sessions = await _context.AiChatSessions
            .Where(s => s.UserId == userId)
            .AsNoTracking()
            .OrderByDescending(s => s.LastMessageAt)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.CreatedAt,
                s.LastMessageAt,
                MessageCount = _context.AiChatMessages.Count(m => m.SessionId == s.Id)
            })
            .ToListAsync();

        return sessions
            .Select(s => new ChatSessionDto
            {
                Id = s.Id,
                Title = s.Title,
                CreatedAt = s.CreatedAt,
                LastMessageAt = s.LastMessageAt,
                MessageCount = s.MessageCount
            })
            .ToList();
    }

    public async Task<ChatSessionDto> CreateSessionAsync(int userId, string? title)
    {
        var session = new AiChatSession
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? "New conversation" : title.Trim()[..Math.Min(title.Trim().Length, 200)],
            CreatedAt = DateTime.UtcNow,
            LastMessageAt = DateTime.UtcNow
        };

        _context.AiChatSessions.Add(session);
        await _context.SaveChangesAsync();

        return new ChatSessionDto
        {
            Id = session.Id,
            Title = session.Title,
            CreatedAt = session.CreatedAt,
            LastMessageAt = session.LastMessageAt,
            MessageCount = 0
        };
    }

    public async Task<List<ChatMessageDto>?> GetMessagesAsync(int userId, int sessionId)
    {
        var owns = await _context.AiChatSessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.UserId == userId);

        if (!owns) return null;

        var messages = await _context.AiChatMessages
            .Where(m => m.SessionId == sessionId)
            .AsNoTracking()
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        return messages.Select(ToDto).ToList();
    }

    public async Task<SendChatMessageResponse> SendMessageAsync(
        int userId,
        int sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.AiChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId)
            ?? throw new InvalidOperationException("Conversation not found.");

        var trimmed = message.Trim();

        var userMessage = new AiChatMessage
        {
            SessionId = session.Id,
            Role = "user",
            Content = trimmed,
            CreatedAt = DateTime.UtcNow
        };
        _context.AiChatMessages.Add(userMessage);

        // The first question names the conversation, so the session list is readable
        // without the user having to name anything.
        if (session.Title == "New conversation" && trimmed.Length > 0)
        {
            session.Title = trimmed.Length <= 60 ? trimmed : trimmed[..60];
        }

        session.LastMessageAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var snapshot = await _analysis.BuildSnapshotAsync(userId);

        var history = await _context.AiChatMessages
            .Where(m => m.SessionId == session.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxTurnsSent)
            .ToListAsync();

        history.Reverse();

        var turns = history
            .Select(m => new ChatTurn(m.Role == "assistant" ? "assistant" : "user", m.Content))
            .ToList();

        string reply;
        var usedFallback = false;

        if (!_provider.IsConfigured)
        {
            reply = BuildUnavailableReply(snapshot);
            usedFallback = true;
        }
        else
        {
            try
            {
                reply = await _provider.CompleteTextAsync(
                    BuildSystemPrompt(snapshot),
                    turns,
                    cancellationToken);
            }
            catch (AiProviderException ex)
            {
                _logger.LogWarning("AI chat fell back to the local reply: {Message}", ex.Message);
                reply = BuildUnavailableReply(snapshot);
                usedFallback = true;
            }
        }

        var assistantMessage = new AiChatMessage
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = reply,
            CreatedAt = DateTime.UtcNow,
            UsedFallback = usedFallback
        };
        _context.AiChatMessages.Add(assistantMessage);
        await _context.SaveChangesAsync();

        return new SendChatMessageResponse
        {
            UserMessage = ToDto(userMessage),
            Reply = ToDto(assistantMessage)
        };
    }

    public async Task<bool> DeleteSessionAsync(int userId, int sessionId)
    {
        var session = await _context.AiChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);

        if (session == null) return false;

        _context.AiChatSessions.Remove(session);
        await _context.SaveChangesAsync();
        return true;
    }

    private static ChatMessageDto ToDto(AiChatMessage m) => new()
    {
        Id = m.Id,
        Role = m.Role,
        Content = m.Content,
        CreatedAt = m.CreatedAt,
        UsedFallback = m.UsedFallback
    };

    private static string BuildSystemPrompt(FinancialSnapshotDto snapshot) =>
        "You are the financial assistant inside Akanti, a personal finance app.\n\n" +
        "You are given the user's financial snapshot: aggregate totals for one month and the month " +
        "before it. Follow these rules:\n\n" +
        "- Use ONLY the figures in the snapshot. Never state a number, percentage or date that is not there.\n" +
        "- If the snapshot does not contain what is needed, say so plainly instead of estimating.\n" +
        "- If you cannot see a transaction the user refers to, say that Akanti only shares totals with you, " +
        "  and that they can check the exact entry on the Expenses or Income page.\n" +
        "- Be concise and practical. Two or three short paragraphs at most.\n" +
        "- Amounts carry no currency symbol. Write them as plain numbers, e.g. 45000.\n" +
        "- Do not give investment, tax or legal advice. Stay on budgeting and cash-flow questions.\n\n" +
        "FINANCIAL SNAPSHOT\n" +
        AiInsightsService.RenderSnapshot(snapshot);

    /// <summary>
    /// Used when no provider is configured or the provider fails. It still answers
    /// from the snapshot rather than leaving the user with nothing.
    /// </summary>
    private static string BuildUnavailableReply(FinancialSnapshotDto snapshot) =>
        "The AI assistant is not available right now, so I cannot answer in detail. " +
        "Here is what I can tell you from your records:\n\n" +
        AiInsightsService.RenderSnapshot(snapshot) + "\n\n" +
        "Once the assistant is configured, ask me anything about your spending.";
}
