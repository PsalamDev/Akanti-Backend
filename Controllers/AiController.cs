using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Akanti.API.DTOs.Ai;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IAiInsightsService _insights;
    private readonly IAiChatService _chat;

    public AiController(IAiInsightsService insights, IAiChatService chat)
    {
        _insights = insights;
        _chat = chat;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// Whether the AI features are usable, and how. Never contains a secret.
    /// </summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public ActionResult<AiStatusDto> GetStatus()
    {
        return Ok(_insights.GetStatus());
    }

    /// <summary>
    /// Insights for the current period. Results are cached briefly.
    /// </summary>
    [HttpGet("insights")]
    [EnableRateLimiting("ai-insights")]
    public async Task<ActionResult<InsightsDto>> GetInsights()
    {
        return Ok(await _insights.GetInsightsAsync(GetUserId()));
    }

    /// <summary>
    /// Regenerates the insights, ignoring the cache.
    /// </summary>
    [HttpPost("insights/refresh")]
    [EnableRateLimiting("ai-insights")]
    public async Task<ActionResult<InsightsDto>> RefreshInsights()
    {
        return Ok(await _insights.GetInsightsAsync(GetUserId(), forceRefresh: true));
    }

    [HttpGet("chat/sessions")]
    public async Task<ActionResult<List<ChatSessionDto>>> GetSessions()
    {
        return Ok(await _chat.GetSessionsAsync(GetUserId()));
    }

    [HttpPost("chat/sessions")]
    public async Task<ActionResult<ChatSessionDto>> CreateSession([FromBody] CreateChatSessionRequest? request)
    {
        var session = await _chat.CreateSessionAsync(GetUserId(), request?.Title);
        return Ok(session);
    }

    [HttpGet("chat/sessions/{id:int}/messages")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetMessages(int id)
    {
        var messages = await _chat.GetMessagesAsync(GetUserId(), id);
        if (messages == null) return NotFound();
        return Ok(messages);
    }

    [HttpPost("chat/sessions/{id:int}/messages")]
    [EnableRateLimiting("ai-chat")]
    public async Task<ActionResult<SendChatMessageResponse>> SendMessage(int id, [FromBody] SendChatMessageRequest request)
    {
        try
        {
            var response = await _chat.SendMessageAsync(GetUserId(), id, request.Message);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("chat/sessions/{id:int}")]
    public async Task<IActionResult> DeleteSession(int id)
    {
        var deleted = await _chat.DeleteSessionAsync(GetUserId(), id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}

public class CreateChatSessionRequest
{
    /// <summary>Optional. The first message names the conversation when this is empty.</summary>
    public string? Title { get; set; }
}
