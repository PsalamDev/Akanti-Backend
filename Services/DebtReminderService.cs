using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class DebtReminderService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DebtReminderService> _logger;

    public DebtReminderService(IServiceProvider serviceProvider, ILogger<DebtReminderService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndSendReminders();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in debt reminder service");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task CheckAndSendReminders()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var now = DateTime.UtcNow;

        var debtsNeedingReminders = await context.Debts
            .Include(d => d.User)
            .Where(d => !d.IsDeleted
                && d.HasReminder
                && d.Type == DebtType.Lent
                && d.Status != DebtStatus.Paid
                && d.DueDate != null
                && d.DueDate <= now.AddDays(d.ReminderDaysBefore))
            .ToListAsync();

        var sentCount = 0;

        foreach (var debt in debtsNeedingReminders)
        {
            if (debt.LastRemindedAt.HasValue && (now - debt.LastRemindedAt.Value).TotalHours < 24)
                continue;

            var daysUntilDue = debt.DueDate!.Value.Date - now.Date;
            var isOverdue = daysUntilDue.TotalDays < 0;
            var statusText = isOverdue
                ? $"was due {Math.Abs((int)daysUntilDue.TotalDays)} days ago"
                : $"is due in {(int)daysUntilDue.TotalDays} day(s)";

            var title = isOverdue ? "Overdue Debt Reminder" : "Upcoming Debt Reminder";
            var message = $"Reminder: ${debt.Amount - debt.AmountPaid:F2} from {debt.PersonName} {statusText}. Please follow up.";

            var notification = new Notification
            {
                UserId = debt.UserId,
                Title = title,
                Message = message,
                Type = NotificationType.DebtReminder,
                IsRead = false,
                ActionUrl = "/debts",
                CreatedAt = now
            };
            context.Notifications.Add(notification);

            var lenderEmail = debt.User?.Email;
            var lenderName = debt.User?.FullName ?? "The lender";
            var personEmail = debt.PersonEmail;
            var personName = debt.PersonName ?? "the other party";

            if (!string.IsNullOrEmpty(lenderEmail) && lenderEmail.Contains('@'))
            {
                var htmlBody = $"""
                    <div style="font-family: Arial, sans-serif; max-width: 500px; margin: 0 auto;">
                        <h2 style="color: #166534;">Akanti - Upcoming Debt Reminder</h2>
                        <p>Hi <strong>{lenderName}</strong>,</p>
                        <p>This is a reminder that the following debt {statusText}:</p>
                        <div style="background: #f9fafb; padding: 15px; border-radius: 8px; margin: 15px 0;">
                            <p><strong>Person:</strong> {personName}</p>
                            <p><strong>Amount Owed:</strong> ${debt.Amount - debt.AmountPaid:F2}</p>
                            <p><strong>Description:</strong> {debt.Description}</p>
                            <p><strong>Due Date:</strong> {debt.DueDate:MMM dd, yyyy}</p>
                        </div>
                        <p>Please follow up with {personName} to collect the payment.</p>
                        <p style="color: #6b7280; font-size: 12px;">- The Akanti Team</p>
                    </div>
                    """;

                await emailService.SendAsync(lenderEmail, $"Akanti: Debt Reminder - {personName}", htmlBody);
            }

            if (!string.IsNullOrEmpty(personEmail) && personEmail.Contains('@'))
            {
                var dueText = isOverdue
                    ? $"was due {Math.Abs((int)daysUntilDue.TotalDays)} day(s) ago"
                    : $"is due in {(int)daysUntilDue.TotalDays} day(s)";

                var htmlBody = $"""
                    <div style="font-family: Arial, sans-serif; max-width: 500px; margin: 0 auto;">
                        <h2 style="color: #166534;">Akanti - Payment Reminder</h2>
                        <p>Hi <strong>{personName}</strong>,</p>
                        <p><strong>{lenderName}</strong> is reminding you about the following debt:</p>
                        <div style="background: #f9fafb; padding: 15px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #dc2626;">
                            <p><strong>Description:</strong> {debt.Description}</p>
                            <p><strong>Amount Remaining:</strong> ${debt.Amount - debt.AmountPaid:F2}</p>
                            <p><strong>Due Date:</strong> {dueText}</p>
                        </div>
                        <p>Please arrange payment at your earliest convenience.</p>
                        <p style="color: #6b7280; font-size: 12px;">Sent via Akanti - Personal Finance Manager</p>
                    </div>
                    """;

                await emailService.SendAsync(personEmail, $"Payment Reminder from {lenderName}", htmlBody);
            }

            debt.LastRemindedAt = now;
            sentCount++;
            _logger.LogInformation("Reminder sent for debt {DebtId} to user {UserId}", debt.Id, debt.UserId);
        }

        if (sentCount > 0)
        {
            await context.SaveChangesAsync();
            _logger.LogInformation("Processed {Count} debt reminders", sentCount);
        }
    }
}
