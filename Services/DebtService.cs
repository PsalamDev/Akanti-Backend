using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Debt;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class DebtService : IDebtService
{
    private readonly ApplicationDbContext _context;
    private readonly IBackgroundEmailQueue _emailQueue;

    public DebtService(ApplicationDbContext context, IBackgroundEmailQueue emailQueue)
    {
        _context = context;
        _emailQueue = emailQueue;
    }

    public async Task<List<DebtDto>> GetAllAsync(int userId, string? type)
    {
        var query = _context.Debts
            .Where(d => d.UserId == userId && !d.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<DebtType>(type, true, out var debtType))
            query = query.Where(d => d.Type == debtType);

        var debts = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();

        return debts.Select(d => new DebtDto
        {
            Id = d.Id,
            Description = d.Description,
            Amount = d.Amount,
            AmountPaid = d.AmountPaid,
            Type = d.Type.ToString(),
            PersonName = d.PersonName,
            PersonEmail = d.PersonEmail,
            DueDate = d.DueDate,
            HasReminder = d.HasReminder,
            ReminderDaysBefore = d.ReminderDaysBefore,
            Status = d.Status.ToString()
        }).ToList();
    }

    public async Task<DebtDto?> GetByIdAsync(int userId, int id)
    {
        var debt = await _context.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && !d.IsDeleted);

        if (debt == null) return null;

        return new DebtDto
        {
            Id = debt.Id,
            Description = debt.Description,
            Amount = debt.Amount,
            AmountPaid = debt.AmountPaid,
            Type = debt.Type.ToString(),
            PersonName = debt.PersonName,
            PersonEmail = debt.PersonEmail,
            DueDate = debt.DueDate,
            HasReminder = debt.HasReminder,
            ReminderDaysBefore = debt.ReminderDaysBefore,
            Status = debt.Status.ToString()
        };
    }

    public async Task<DebtDto> CreateAsync(int userId, CreateDebtRequest request)
    {
        var debt = new Debt
        {
            UserId = userId,
            Description = request.Description,
            Amount = request.Amount,
            Type = Enum.TryParse<DebtType>(request.Type, true, out var t) ? t : DebtType.Borrowed,
            PersonName = request.PersonName,
            PersonEmail = request.PersonEmail,
            DueDate = request.DueDate,
            HasReminder = request.HasReminder,
            ReminderDaysBefore = request.ReminderDaysBefore
        };

        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        _context.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = "Debt Created",
            Message = $"New {debt.Type} debt of ₦{debt.Amount:N2} with {debt.PersonName} has been added.",
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return new DebtDto
        {
            Id = debt.Id,
            Description = debt.Description,
            Amount = debt.Amount,
            AmountPaid = debt.AmountPaid,
            Type = debt.Type.ToString(),
            PersonName = debt.PersonName,
            PersonEmail = debt.PersonEmail,
            DueDate = debt.DueDate,
            HasReminder = debt.HasReminder,
            ReminderDaysBefore = debt.ReminderDaysBefore,
            Status = debt.Status.ToString()
        };
    }

    public async Task<bool> UpdateAsync(int userId, int id, CreateDebtRequest request)
    {
        var debt = await _context.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && !d.IsDeleted);

        if (debt == null) return false;

        debt.Description = request.Description;
        debt.Amount = request.Amount;
        debt.Type = Enum.TryParse<DebtType>(request.Type, true, out var t) ? t : DebtType.Borrowed;
        debt.PersonName = request.PersonName;
        debt.PersonEmail = request.PersonEmail;
        debt.DueDate = request.DueDate;
        debt.HasReminder = request.HasReminder;
        debt.ReminderDaysBefore = request.ReminderDaysBefore;
        debt.UpdatedAt = DateTime.UtcNow;

        _context.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = "Debt Updated",
            Message = $"Debt record for {debt.PersonName} has been updated.",
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var debt = await _context.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && !d.IsDeleted);

        if (debt == null) return false;

        debt.IsDeleted = true;
        debt.UpdatedAt = DateTime.UtcNow;

        _context.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = "Debt Deleted",
            Message = $"Debt record for {debt.PersonName} (₦{debt.Amount:N2}) has been deleted.",
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<(int Id, decimal AmountPaid, string Status)?> RecordPaymentAsync(int userId, int id, UpdateDebtPaymentRequest request)
    {
        var debt = await _context.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && !d.IsDeleted);

        if (debt == null) return null;

        debt.AmountPaid += request.Amount;
        debt.Status = debt.AmountPaid >= debt.Amount ? DebtStatus.Paid : DebtStatus.PartiallyPaid;
        debt.UpdatedAt = DateTime.UtcNow;

        var statusMsg = debt.Status == DebtStatus.Paid ? "fully paid" : $"partially paid (₦{debt.AmountPaid:N2} of ₦{debt.Amount:N2})";
        _context.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = "Payment Recorded",
            Message = $"₦{request.Amount:N2} payment recorded for {debt.PersonName}. Debt is now {statusMsg}.",
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return (debt.Id, debt.AmountPaid, debt.Status.ToString());
    }

    public async Task<List<DebtDto>> GetUpcomingAsync(int userId, int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(days);
        var debts = await _context.Debts
            .Where(d => d.UserId == userId && !d.IsDeleted && d.DueDate.HasValue && d.DueDate <= cutoff && d.Status != DebtStatus.Paid)
            .OrderBy(d => d.DueDate)
            .ToListAsync();

        return debts.Select(d => new DebtDto
        {
            Id = d.Id,
            Description = d.Description,
            Amount = d.Amount,
            AmountPaid = d.AmountPaid,
            Type = d.Type.ToString(),
            PersonName = d.PersonName,
            PersonEmail = d.PersonEmail,
            DueDate = d.DueDate,
            HasReminder = d.HasReminder,
            ReminderDaysBefore = d.ReminderDaysBefore,
            Status = d.Status.ToString()
        }).ToList();
    }

    public async Task SendReminderAsync(int userId, int id)
    {
        var debt = await _context.Debts
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && !d.IsDeleted);

        if (debt == null) return;

        var remaining = debt.Amount - debt.AmountPaid;
        var now = DateTime.UtcNow;
        var isOverdue = debt.DueDate.HasValue && debt.DueDate.Value < now;
        var statusText = isOverdue
            ? $"was due on {debt.DueDate:MMM dd, yyyy}"
            : $"is due on {debt.DueDate:MMM dd, yyyy}";

        var title = isOverdue ? "Overdue Debt Reminder" : "Debt Reminder";
        var msg = $"Reminder: ₦{remaining:N2} from {debt.PersonName} ({debt.Description}).";

        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Message = msg,
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = now
        };
        _context.Notifications.Add(notification);

        // If the person being reminded has an Akanti account, put the same reminder
        // in their notification bell as well. Saved together with the line above.
        await DebtReminderNotifier.AddDebtorNotificationAsync(_context, debt, now);

        await _context.SaveChangesAsync();

        var lenderEmail = debt.User?.Email;
        var lenderName = debt.User?.FullName ?? "The lender";
        var personEmail = debt.PersonEmail;
        var personName = debt.PersonName ?? "the other party";

        if (!string.IsNullOrEmpty(lenderEmail) && lenderEmail.Contains('@'))
        {
            var htmlBody = $"""
                <div style="font-family: Arial, sans-serif; max-width: 500px; margin: 0 auto;">
                    <h2 style="color: #166534;">Akanti - Debt Reminder (You)</h2>
                    <p>Hi <strong>{lenderName}</strong>,</p>
                    <p>You sent a payment reminder to <strong>{personName}</strong> for the following debt:</p>
                    <div style="background: #f9fafb; padding: 15px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #166534;">
                        <p><strong>Description:</strong> {debt.Description}</p>
                        <p><strong>Amount Remaining:</strong> ₦{remaining:N2}</p>
                        <p><strong>Due Date:</strong> {statusText}</p>
                    </div>
                    <p style="color: #6b7280; font-size: 12px;">Sent via Akanti - Personal Finance Manager</p>
                </div>
                """;

            await _emailQueue.EnqueueAsync(lenderEmail, $"Reminder sent to {personName}", htmlBody);
        }

        if (!string.IsNullOrEmpty(personEmail) && personEmail.Contains('@'))
        {
            var htmlBody = $"""
                <div style="font-family: Arial, sans-serif; max-width: 500px; margin: 0 auto;">
                    <h2 style="color: #166534;">Akanti - Payment Reminder</h2>
                    <p>Hi <strong>{personName}</strong>,</p>
                    <p><strong>{lenderName}</strong> is reminding you about the following debt:</p>
                    <div style="background: #f9fafb; padding: 15px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #dc2626;">
                        <p><strong>Description:</strong> {debt.Description}</p>
                        <p><strong>Amount Remaining:</strong> ₦{remaining:N2}</p>
                        <p><strong>Due Date:</strong> {statusText}</p>
                    </div>
                    <p>Please arrange payment at your earliest convenience.</p>
                    <p style="color: #6b7280; font-size: 12px;">Sent via Akanti - Personal Finance Manager</p>
                </div>
                """;

            await _emailQueue.EnqueueAsync(personEmail, $"Payment Reminder from {lenderName}", htmlBody);
        }
    }
}
