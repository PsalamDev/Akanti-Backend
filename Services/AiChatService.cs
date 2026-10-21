using System.Text;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Ai;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

/// <summary>
/// The conversational assistant.
///
/// Conversations are stored server-side, so they survive a reload and are available
/// on any device. Every turn is grounded in the same aggregate snapshot the rest of
/// the app uses — the assistant never sees transaction titles, descriptions,
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
            Content = Normalise(trimmed),
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
        var findings = _analysis.BuildFindings(snapshot);

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
            reply = BuildUnavailableReply(snapshot, findings);
            usedFallback = true;
        }
        else
        {
            try
            {
                reply = await _provider.CompleteTextAsync(
                    BuildSystemPrompt(snapshot, findings),
                    turns,
                    cancellationToken);

                // The model is told to use only the figures in the snapshot, but it
                // does occasionally produce one that is not there. An invented amount
                // is worse than a plain answer, because the user has no way to tell
                // it apart from a real one, so the reply is checked before it is
                // stored.
                var unsupported = AiFigureGuard.FindUnsupportedFigures(reply, snapshot);
                if (unsupported.Count > 0)
                {
                    _logger.LogWarning(
                        "AI chat reply mentioned figures that are not in the snapshot ({Figures}); using the records-based answer.",
                        string.Join(", ", unsupported));
                    reply = BuildRecordsReply(snapshot, findings);
                    usedFallback = true;
                }
            }
            catch (AiProviderException ex)
            {
                _logger.LogWarning("AI chat fell back to the records-based answer: {Message}", ex.Message);
                reply = BuildUnavailableReply(snapshot, findings);
                usedFallback = true;
            }
        }

        var assistantMessage = new AiChatMessage
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = Normalise(reply),
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

    /// <summary>
    /// Uses Unix line endings throughout. A reply built on Windows would otherwise
    /// be stored with CRLF and one built on Linux with LF, so the same conversation
    /// would look different depending on where the server happened to be running.
    /// </summary>
    private static string Normalise(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n");

    private static string BuildSystemPrompt(FinancialSnapshotDto snapshot, FindingsDto findings)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are the financial assistant inside Akanti, a personal finance app.");
        sb.AppendLine();
        sb.AppendLine("You are given the user's financial snapshot: aggregate totals for one month and the month before it. Follow these rules:");
        sb.AppendLine();
        sb.AppendLine("- Answer in plain language, like a person who knows their own money. Two or three short paragraphs at most.");
        sb.AppendLine("- Use ONLY the figures in the snapshot. Never state a number, percentage or date that is not there.");
        sb.AppendLine("- Never invent an amount to aim for. Give direction instead: \"try moving a little from dining out\", not \"set aside 50000\".");
        sb.AppendLine("- If the snapshot does not contain what is needed, say so plainly instead of estimating.");
        sb.AppendLine("- If you cannot see a transaction the user refers to, say that Akanti only shares totals with you, " +
                      "and that they can check the exact entry on the Expenses or Income page.");
        sb.AppendLine("- Amounts carry no currency symbol. Write them as plain numbers, e.g. 45000.");
        sb.AppendLine("- Do not give investment, tax or legal advice. Stay on budgeting and cash-flow questions.");
        sb.AppendLine();
        sb.AppendLine("FINANCIAL SNAPSHOT");
        sb.AppendLine(FinancialAnalysisService.RenderSnapshot(snapshot));

        var items = findings.Highlights.Concat(findings.Warnings).Concat(findings.Recommendations).ToList();
        if (items.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("WHAT THE APP ALREADY FOUND (computed from the snapshot above - use it, and do not contradict it)");
            foreach (var item in items)
            {
                sb.AppendLine($"- [{item.Severity}] {item.Title}: {item.Detail}");
            }
        }

        return Normalise(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Used when no provider is configured or the provider fails. It still answers
    /// from the user's own records rather than leaving them with nothing.
    /// </summary>
    private static string BuildUnavailableReply(FinancialSnapshotDto snapshot, FindingsDto findings) =>
        "The AI assistant is not available right now, so I cannot go into detail. " +
        "Here is what your records say:\n\n" +
        BuildRecordsBody(snapshot, findings);

    /// <summary>
    /// Used when the model's answer mentioned a figure that the records do not
    /// support. It says so plainly rather than quietly showing a wrong number.
    /// </summary>
    private static string BuildRecordsReply(FinancialSnapshotDto snapshot, FindingsDto findings) =>
        "I could not match every figure in that answer to your records, so here is what I can confirm:\n\n" +
        BuildRecordsBody(snapshot, findings);

    /// <summary>
    /// The answer built entirely from the user's own records: the findings the app
    /// computed, then the totals they came from.
    /// </summary>
    private static string BuildRecordsBody(FinancialSnapshotDto snapshot, FindingsDto findings)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(findings.Summary))
        {
            sb.AppendLine(findings.Summary);
            sb.AppendLine();
        }

        foreach (var item in findings.Highlights.Concat(findings.Warnings).Concat(findings.Recommendations))
        {
            sb.AppendLine($"- {item.Title}: {item.Detail}");
        }

        if (findings.Highlights.Count + findings.Warnings.Count + findings.Recommendations.Count > 0)
        {
            sb.AppendLine();
        }

        sb.AppendLine(FinancialAnalysisService.RenderSnapshot(snapshot));

        return sb.ToString().TrimEnd();
    }
}
