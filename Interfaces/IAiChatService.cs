using Akanti.API.DTOs.Ai;

namespace Akanti.API.Interfaces;

public interface IAiChatService
{
    Task<List<ChatSessionDto>> GetSessionsAsync(int userId);

    Task<ChatSessionDto> CreateSessionAsync(int userId, string? title);

    Task<List<ChatMessageDto>?> GetMessagesAsync(int userId, int sessionId);

    /// <summary>
    /// Records the user's message, asks the model for a reply grounded in their
    /// financial snapshot, and records the reply.
    /// </summary>
    Task<SendChatMessageResponse> SendMessageAsync(int userId, int sessionId, string message, CancellationToken cancellationToken = default);

    Task<bool> DeleteSessionAsync(int userId, int sessionId);
}
