using System.Security.Claims;
using Akanti.API.Models;
using Akanti.API.Services;

namespace Akanti.API.Middleware;

public class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuditLogQueue _auditLogQueue;

    public AuditMiddleware(RequestDelegate next, IAuditLogQueue auditLogQueue)
    {
        _next = next;
        _auditLogQueue = auditLogQueue;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        if (context.Request.Method != "GET" && context.Request.Method != "HEAD" &&
            context.Response.StatusCode < 400)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var entry = new AuditLog
            {
                UserId = userIdClaim != null && int.TryParse(userIdClaim, out var parsed) ? parsed : null,
                Action = context.Request.Method,
                EntityName = GetEntityFromPath(context.Request.Path),
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.UtcNow
            };

            await _auditLogQueue.EnqueueAsync(entry);
        }
    }

    private static string GetEntityFromPath(PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments?.Length > 0 ? segments[0] : "Unknown";
    }
}
