using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.Models;

namespace Akanti.API.Middleware;

public class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        await _next(context);

        if (context.Request.Method != "GET" && context.Request.Method != "HEAD" &&
            context.Response.StatusCode < 400)
        {
            try
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var auditLog = new AuditLog
                {
                    UserId = userId != null ? int.Parse(userId) : null,
                    Action = context.Request.Method,
                    EntityName = GetEntityFromPath(context.Request.Path),
                    IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.AuditLogs.Add(auditLog);
                await dbContext.SaveChangesAsync();
            }
            catch
            {
            }
        }
    }

    private static string GetEntityFromPath(PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments?.Length > 0 ? segments[0] : "Unknown";
    }
}
