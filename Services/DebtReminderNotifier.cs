using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.Models;

namespace Akanti.API.Services;

/// <summary>
/// Delivers a debt reminder to the other party's own Akanti account.
///
/// A reminder has always gone out by email when the lender typed an address. When
/// that same address belongs to a registered user, the reminder can also land in
/// their notification bell, which is more immediate and cannot be lost to a spam
/// folder. Nothing new is exposed: the lender already chose to send them an email
/// with the same amount, description and due date.
/// </summary>
internal static class DebtReminderNotifier
{
    /// <summary>
    /// Adds an in-app notification for the person named on the debt, if their email
    /// belongs to an active Akanti account other than the lender's. Returns the
    /// notified user's id, or <c>null</c> when there is nobody to notify.
    ///
    /// The caller owns the <see cref="ApplicationDbContext"/> and is responsible for
    /// saving the change, so the notification is committed in the same transaction as
    /// the rest of the reminder.
    /// </summary>
    public static async Task<int?> AddDebtorNotificationAsync(
        ApplicationDbContext context,
        Debt debt,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var address = Normalise(debt.PersonEmail);
        if (address is null) return null;

        // Only a "Lent" debt leaves the other party owing money, and a paid debt
        // needs no reminder. Guards the manual endpoint, which the UI already hides
        // in these cases but an API caller could still hit.
        if (debt.Type != DebtType.Lent || debt.Status == DebtStatus.Paid) return null;

        // Emails are stored lowercased (AuthService.NormalizeEmail), so an equality
        // comparison is both correct and index-friendly.
        var debtorId = await context.Users
            .AsNoTracking()
            .Where(u => u.Email == address && u.IsActive)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // A lender can put their own address on a debt; do not notify them twice.
        if (debtorId is null || debtorId == debt.UserId) return null;

        var remaining = debt.Amount - debt.AmountPaid;
        var lenderName = string.IsNullOrWhiteSpace(debt.User?.FullName)
            ? "Someone"
            : debt.User.FullName;

        var dueText = string.Empty;
        if (debt.DueDate.HasValue)
        {
            var due = debt.DueDate.Value;
            dueText = due < now
                ? $", which was due on {due:MMM dd, yyyy}"
                : $", due on {due:MMM dd, yyyy}";
        }

        context.Notifications.Add(new Notification
        {
            UserId = debtorId.Value,
            Title = "Payment Reminder",
            Message = $"{lenderName} is asking you about {debt.Description} — " +
                      $"₦{remaining:N2} outstanding{dueText}.",
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/notifications",
            CreatedAt = now
        });

        return debtorId.Value;
    }

    private static string? Normalise(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var trimmed = email.Trim().ToLowerInvariant();
        return trimmed.Contains('@') ? trimmed : null;
    }
}
