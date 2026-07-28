using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Debt;
using Akanti.API.Models;
using Akanti.API.Services;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DebtController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;

    public DebtController(ApplicationDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<DebtDto>>> GetAll([FromQuery] string? type)
    {
        var query = _context.Debts
            .Where(d => d.UserId == GetUserId() && !d.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<DebtType>(type, true, out var debtType))
            query = query.Where(d => d.Type == debtType);

        var debts = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();

        return Ok(debts.Select(d => new DebtDto
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
        }));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DebtDto>> GetById(int id)
    {
        var debt = await _context.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == GetUserId() && !d.IsDeleted);

        if (debt == null) return NotFound();

        return Ok(new DebtDto
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
        });
    }

    [HttpPost]
    public async Task<ActionResult<DebtDto>> Create([FromBody] CreateDebtRequest request)
    {
        var debt = new Debt
        {
            UserId = GetUserId(),
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
            UserId = GetUserId(),
            Title = "Debt Created",
            Message = $"New {debt.Type} debt of ₦{debt.Amount:N2} with {debt.PersonName} has been added.",
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = debt.Id }, new DebtDto
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
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateDebtRequest request)
    {
        var debt = await _context.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == GetUserId() && !d.IsDeleted);

        if (debt == null) return NotFound();

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
            UserId = GetUserId(),
            Title = "Debt Updated",
            Message = $"Debt record for {debt.PersonName} has been updated.",
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/payment")]
    public async Task<IActionResult> RecordPayment(int id, [FromBody] UpdateDebtPaymentRequest request)
    {
        var debt = await _context.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == GetUserId() && !d.IsDeleted);

        if (debt == null) return NotFound();

        debt.AmountPaid += request.Amount;
        debt.Status = debt.AmountPaid >= debt.Amount ? DebtStatus.Paid : DebtStatus.PartiallyPaid;
        debt.UpdatedAt = DateTime.UtcNow;

        var statusMsg = debt.Status == DebtStatus.Paid ? "fully paid" : $"partially paid (₦{debt.AmountPaid:N2} of ₦{debt.Amount:N2})";
        _context.Notifications.Add(new Notification
        {
            UserId = GetUserId(),
            Title = "Payment Recorded",
            Message = $"₦{request.Amount:N2} payment recorded for {debt.PersonName}. Debt is now {statusMsg}.",
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return Ok(new { debt.Id, debt.AmountPaid, debt.Status });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var debt = await _context.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == GetUserId() && !d.IsDeleted);

        if (debt == null) return NotFound();

        debt.IsDeleted = true;
        debt.UpdatedAt = DateTime.UtcNow;

        _context.Notifications.Add(new Notification
        {
            UserId = GetUserId(),
            Title = "Debt Deleted",
            Message = $"Debt record for {debt.PersonName} (₦{debt.Amount:N2}) has been deleted.",
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<List<DebtDto>>> GetUpcoming([FromQuery] int days = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(days);
        var debts = await _context.Debts
            .Where(d => d.UserId == GetUserId() && !d.IsDeleted && d.DueDate.HasValue && d.DueDate <= cutoff && d.Status != DebtStatus.Paid)
            .OrderBy(d => d.DueDate)
            .ToListAsync();

        return Ok(debts.Select(d => new DebtDto
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
        }));
    }

    [HttpPost("{id}/remind")]
    public async Task<IActionResult> SendReminder(int id)
    {
        var debt = await _context.Debts
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == GetUserId() && !d.IsDeleted);

        if (debt == null) return NotFound();

        var remaining = debt.Amount - debt.AmountPaid;
        var now = DateTime.UtcNow;
        var isOverdue = debt.DueDate.HasValue && debt.DueDate.Value < now;
        var statusText = isOverdue
            ? $"was due on {debt.DueDate:MMM dd, yyyy}"
            : $"is due on {debt.DueDate:MMM dd, yyyy}";

        var title = isOverdue ? "Overdue Debt Reminder" : "Debt Reminder";
        var msg = $"Reminder: ${remaining:F2} from {debt.PersonName} ({debt.Description}).";

        var notification = new Notification
        {
            UserId = GetUserId(),
            Title = title,
            Message = msg,
            Type = NotificationType.DebtReminder,
            IsRead = false,
            ActionUrl = "/dashboard/debts",
            CreatedAt = now
        };
        _context.Notifications.Add(notification);
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
                        <p><strong>Amount Remaining:</strong> ${remaining:F2}</p>
                        <p><strong>Due Date:</strong> {statusText}</p>
                    </div>
                    <p style="color: #6b7280; font-size: 12px;">Sent via Akanti - Personal Finance Manager</p>
                </div>
                """;

            await _emailService.SendAsync(lenderEmail, $"Reminder sent to {personName}", htmlBody);
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
                        <p><strong>Amount Remaining:</strong> ${remaining:F2}</p>
                        <p><strong>Due Date:</strong> {statusText}</p>
                    </div>
                    <p>Please arrange payment at your earliest convenience.</p>
                    <p style="color: #6b7280; font-size: 12px;">Sent via Akanti - Personal Finance Manager</p>
                </div>
                """;

            await _emailService.SendAsync(personEmail, $"Payment Reminder from {lenderName}", htmlBody);
        }

        return Ok(new { message = "Reminder sent successfully." });
    }
}
