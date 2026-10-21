using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Invoice;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class InvoiceService : IInvoiceService
{
    private readonly ApplicationDbContext _context;

    public InvoiceService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<InvoiceDto>> GetAllAsync(int userId, string? status, int? customerId)
    {
        var query = _context.Invoices
            .Where(i => i.UserId == userId && !i.IsDeleted)
            .Include(i => i.Customer)
            .Include(i => i.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Unknown values are ignored rather than silently returning everything.
            if (Enum.TryParse<InvoiceStatus>(status, true, out var parsed))
            {
                query = query.Where(i => i.Status == parsed);
            }
            else
            {
                return new List<InvoiceDto>();
            }
        }

        if (customerId.HasValue)
        {
            query = query.Where(i => i.CustomerId == customerId.Value);
        }

        // Unpaid work first, then by how soon it is due.
        var invoices = await query
            .OrderBy(i => i.Status == InvoiceStatus.Paid)
            .ThenBy(i => i.DueDate)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

        return invoices.Select(ToDto).ToList();
    }

    public async Task<InvoiceDto?> GetByIdAsync(int userId, int id)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId && !i.IsDeleted);

        return invoice == null ? null : ToDto(invoice);
    }

    public async Task<InvoiceDto> CreateAsync(int userId, CreateInvoiceRequest request)
    {
        Validate(request);

        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.UserId == userId && !c.IsDeleted)
            ?? throw new InvalidOperationException("Customer not found.");

        var invoice = new Invoice
        {
            UserId = userId,
            CustomerId = customer.Id,
            Number = await NextNumberAsync(userId),
            Status = InvoiceStatus.Draft,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            IssueDate = request.IssueDate,
            DueDate = request.DueDate,
            Notes = request.Notes?.Trim(),
            Tax = request.Tax
        };

        ApplyItems(invoice, request);
        ApplyTotals(invoice);

        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync();

        invoice.Customer = customer;
        return ToDto(invoice);
    }

    public async Task<InvoiceDto?> UpdateAsync(int userId, int id, CreateInvoiceRequest request)
    {
        Validate(request);

        var invoice = await _context.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId && !i.IsDeleted);

        if (invoice == null) return null;

        // Once PayPal owns the invoice, its content is frozen. Changing it locally would
        // immediately diverge from what the customer was sent.
        if (invoice.PayPalInvoiceId != null || invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException(
                "Only draft invoices can be edited. Cancel and reissue a sent invoice instead.");
        }

        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.UserId == userId && !c.IsDeleted)
            ?? throw new InvalidOperationException("Customer not found.");

        invoice.CustomerId = customer.Id;
        invoice.Currency = request.Currency.Trim().ToUpperInvariant();
        invoice.IssueDate = request.IssueDate;
        invoice.DueDate = request.DueDate;
        invoice.Notes = request.Notes?.Trim();
        invoice.Tax = request.Tax;
        invoice.UpdatedAt = DateTime.UtcNow;

        // Replace the lines wholesale; simpler and safer than diffing.
        _context.InvoiceItems.RemoveRange(invoice.Items);
        invoice.Items.Clear();
        ApplyItems(invoice, request);
        ApplyTotals(invoice);

        await _context.SaveChangesAsync();

        invoice.Customer = customer;
        return ToDto(invoice);
    }

    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var invoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId && !i.IsDeleted);

        if (invoice == null) return false;

        // A sent invoice is a record of something the customer holds; it is cancelled,
        // never quietly deleted.
        if (invoice.PayPalInvoiceId != null || invoice.Status != InvoiceStatus.Draft)
        {
            return false;
        }

        invoice.IsDeleted = true;
        invoice.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<InvoiceSummaryDto> GetSummaryAsync(int userId)
    {
        var invoices = await _context.Invoices
            .Where(i => i.UserId == userId && !i.IsDeleted)
            .Select(i => new { i.Status, i.Total, i.DueDate })
            .ToListAsync();

        var now = DateTime.UtcNow;

        return new InvoiceSummaryDto
        {
            TotalCount = invoices.Count,
            DraftCount = invoices.Count(i => i.Status == InvoiceStatus.Draft),
            SentCount = invoices.Count(i => i.Status == InvoiceStatus.Sent),
            PaidCount = invoices.Count(i => i.Status == InvoiceStatus.Paid),
            OverdueCount = invoices.Count(i => i.Status == InvoiceStatus.Sent && i.DueDate < now),
            OutstandingAmount = invoices
                .Where(i => i.Status == InvoiceStatus.Sent)
                .Sum(i => i.Total),
            PaidAmount = invoices
                .Where(i => i.Status == InvoiceStatus.Paid)
                .Sum(i => i.Total)
        };
    }

    /// <summary>
    /// Sequential per-user number (INV-0001). Not concurrency-proof on its own; the
    /// filtered unique index on (UserId, Number) is the guarantee, and a caller that
    /// races is expected to retry.
    /// </summary>
    private async Task<string> NextNumberAsync(int userId)
    {
        var count = await _context.Invoices
            .IgnoreQueryFilters()
            .CountAsync(i => i.UserId == userId);

        return $"INV-{count + 1:D4}";
    }

    private static void Validate(CreateInvoiceRequest request)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw new InvalidOperationException("At least one line item is required.");
        }

        if (request.DueDate.Date < request.IssueDate.Date)
        {
            throw new InvalidOperationException("Due date cannot be earlier than the issue date.");
        }

        if (request.Currency.Trim().Length != 3)
        {
            throw new InvalidOperationException("Currency must be a three-letter ISO 4217 code.");
        }
    }

    private static void ApplyItems(Invoice invoice, CreateInvoiceRequest request)
    {
        foreach (var line in request.Items)
        {
            var quantity = line.Quantity <= 0 ? 1m : line.Quantity;

            invoice.Items.Add(new InvoiceItem
            {
                Description = line.Description.Trim(),
                Quantity = quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = Round(quantity * line.UnitPrice)
            });
        }
    }

    /// <summary>Totals are always derived server-side; client values are never trusted.</summary>
    private static void ApplyTotals(Invoice invoice)
    {
        invoice.Subtotal = Round(invoice.Items.Sum(i => i.LineTotal));
        invoice.Tax = Round(invoice.Tax);
        invoice.Total = Round(invoice.Subtotal + invoice.Tax);
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static InvoiceDto ToDto(Invoice invoice)
    {
        return new InvoiceDto
        {
            Id = invoice.Id,
            Number = invoice.Number,
            Status = invoice.Status.ToString(),
            CustomerId = invoice.CustomerId,
            CustomerName = invoice.Customer?.Name ?? string.Empty,
            CustomerEmail = invoice.Customer?.Email ?? string.Empty,
            Currency = invoice.Currency,
            Subtotal = invoice.Subtotal,
            Tax = invoice.Tax,
            Total = invoice.Total,
            IssueDate = invoice.IssueDate,
            DueDate = invoice.DueDate,
            Notes = invoice.Notes,
            PayPalInvoiceId = invoice.PayPalInvoiceId,
            PayPalStatus = invoice.PayPalStatus,
            RecipientViewUrl = invoice.RecipientViewUrl,
            PaidAt = invoice.PaidAt,
            IncomeId = invoice.IncomeId,
            CreatedAt = invoice.CreatedAt,
            UpdatedAt = invoice.UpdatedAt,
            Items = invoice.Items
                .OrderBy(i => i.Id)
                .Select(i => new InvoiceItemDto
                {
                    Id = i.Id,
                    Description = i.Description,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    LineTotal = i.LineTotal
                })
                .ToList()
        };
    }
}
