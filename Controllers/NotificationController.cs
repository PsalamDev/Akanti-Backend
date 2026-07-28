using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.Models;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public NotificationController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<Notification>>> GetAll([FromQuery] bool? unreadOnly)
    {
        var query = _context.Notifications
            .Where(n => n.UserId == GetUserId())
            .AsQueryable();

        if (unreadOnly == true)
            query = query.Where(n => !n.IsRead);

        var notifications = await query.OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync();
        return Ok(notifications);
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == GetUserId());

        if (notification == null) return NotFound();

        notification.IsRead = true;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var notifications = await _context.Notifications
            .Where(n => n.UserId == GetUserId() && !n.IsRead)
            .ToListAsync();

        foreach (var n in notifications)
            n.IsRead = true;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> GetUnreadCount()
    {
        var count = await _context.Notifications
            .CountAsync(n => n.UserId == GetUserId() && !n.IsRead);
        return Ok(new { count });
    }
}