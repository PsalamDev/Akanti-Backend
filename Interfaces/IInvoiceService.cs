using Akanti.API.DTOs.Invoice;

namespace Akanti.API.Interfaces;

public interface IInvoiceService
{
    Task<List<InvoiceDto>> GetAllAsync(int userId, string? status, int? customerId);

    Task<InvoiceDto?> GetByIdAsync(int userId, int id);

    /// <summary>Creates a local draft invoice. Nothing is sent to PayPal here.</summary>
    Task<InvoiceDto> CreateAsync(int userId, CreateInvoiceRequest request);

    /// <summary>
    /// Updates an invoice that has not been sent to PayPal. Sent invoices are immutable
    /// and must be cancelled and reissued instead (see PAYPAL-INVOICING.md).
    /// </summary>
    Task<InvoiceDto?> UpdateAsync(int userId, int id, CreateInvoiceRequest request);

    /// <summary>Soft-deletes an invoice. Only unsent drafts can be deleted.</summary>
    Task<bool> DeleteAsync(int userId, int id);

    Task<InvoiceSummaryDto> GetSummaryAsync(int userId);
}
