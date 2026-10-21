using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Akanti.API.DTOs.Invoice;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;

    public InvoicesController(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<InvoiceDto>>> GetAll([FromQuery] string? status, [FromQuery] int? customerId)
    {
        return Ok(await _invoiceService.GetAllAsync(GetUserId(), status, customerId));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<InvoiceSummaryDto>> GetSummary()
    {
        return Ok(await _invoiceService.GetSummaryAsync(GetUserId()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<InvoiceDto>> GetById(int id)
    {
        var invoice = await _invoiceService.GetByIdAsync(GetUserId(), id);
        if (invoice == null) return NotFound();
        return Ok(invoice);
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceDto>> Create([FromBody] CreateInvoiceRequest request)
    {
        try
        {
            var invoice = await _invoiceService.CreateAsync(GetUserId(), request);
            return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, invoice);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<InvoiceDto>> Update(int id, [FromBody] CreateInvoiceRequest request)
    {
        try
        {
            var invoice = await _invoiceService.UpdateAsync(GetUserId(), id, request);
            if (invoice == null) return NotFound();
            return Ok(invoice);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _invoiceService.DeleteAsync(GetUserId(), id);
        if (!deleted)
        {
            return BadRequest(new { message = "Invoice not found, or it has already been sent and can only be cancelled." });
        }
        return NoContent();
    }
}
