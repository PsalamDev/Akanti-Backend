using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<Notification>>> GetAll([FromQuery] bool? unreadOnly)
    {
        var notifications = await _notificationService.GetAllAsync(GetUserId(), unreadOnly);
        return Ok(notifications);
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var marked = await _notificationService.MarkAsReadAsync(GetUserId(), id);
        if (!marked) return NotFound();
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await _notificationService.MarkAllAsReadAsync(GetUserId());
        return NoContent();
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> GetUnreadCount()
    {
        var count = await _notificationService.GetUnreadCountAsync(GetUserId());
        return Ok(new { count });
    }
}
