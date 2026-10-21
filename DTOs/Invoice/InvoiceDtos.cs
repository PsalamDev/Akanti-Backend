using System.ComponentModel.DataAnnotations;

namespace Akanti.API.DTOs.Invoice;

public class InvoiceItemRequest
{
    [Required, MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 1_000_000)]
    public decimal Quantity { get; set; } = 1m;

    [Range(0, 1_000_000_000)]
    public decimal UnitPrice { get; set; }
}

public class CreateInvoiceRequest
{
    public int CustomerId { get; set; }

    /// <summary>ISO 4217 code. Defaults to USD.</summary>
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [Required]
    public DateTime IssueDate { get; set; }

    [Required]
    public DateTime DueDate { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Flat tax amount applied to the invoice, not a percentage.</summary>
    [Range(0, 1_000_000_000)]
    public decimal Tax { get; set; }

    [Required, MinLength(1, ErrorMessage = "At least one line item is required.")]
    public List<InvoiceItemRequest> Items { get; set; } = new();
}

public class InvoiceItemDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class InvoiceDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public string? Notes { get; set; }

    public string? PayPalInvoiceId { get; set; }
    public string? PayPalStatus { get; set; }
    public string? RecipientViewUrl { get; set; }

    public DateTime? PaidAt { get; set; }
    public int? IncomeId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<InvoiceItemDto> Items { get; set; } = new();
}

/// <summary>Totals across invoices, for a dashboard tile.</summary>
public class InvoiceSummaryDto
{
    public int TotalCount { get; set; }
    public int DraftCount { get; set; }
    public int SentCount { get; set; }
    public int PaidCount { get; set; }
    public int OverdueCount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal PaidAmount { get; set; }
}
