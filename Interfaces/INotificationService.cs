using Akanti.API.Models;

namespace Akanti.API.Interfaces;

public interface INotificationService
{
    Task<List<Notification>> GetAllAsync(int userId, bool? unreadOnly);
    Task<bool> MarkAsReadAsync(int userId, int id);
    Task MarkAllAsReadAsync(int userId);
    Task<bool> DeleteAsync(int userId, int id);
    Task<int> GetUnreadCountAsync(int userId);
}
