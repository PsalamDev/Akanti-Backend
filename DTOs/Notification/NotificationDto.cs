using Akanti.API.Models;

namespace Akanti.API.DTOs.Notifications;

/// <summary>
/// What the notification bell reads. The type is sent as a name rather than as the
/// enum's underlying number, so the client can style each kind without having to
/// know the enum's ordering.
/// </summary>
public class NotificationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string? ActionUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public static NotificationDto From(Notification notification) => new()
    {
        Id = notification.Id,
        Title = notification.Title,
        Message = notification.Message,
        Type = notification.Type.ToString(),
        IsRead = notification.IsRead,
        ActionUrl = notification.ActionUrl,
        CreatedAt = notification.CreatedAt
    };
}
