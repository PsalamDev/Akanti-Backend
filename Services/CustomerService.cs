using Microsoft.EntityFrameworkCore;
using Akanti.API.Data;
using Akanti.API.DTOs.Invoice;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class CustomerService : ICustomerService
{
    private readonly ApplicationDbContext _context;

    public CustomerService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<CustomerDto>> GetAllAsync(int userId, string? search)
    {
        var query = _context.Customers
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{term}%")
                                  || EF.Functions.ILike(c.Email, $"%{term}%"));
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new CustomerDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Phone = c.Phone,
                PayPalCustomerId = c.PayPalCustomerId,
                InvoiceCount = c.Invoices.Count(i => !i.IsDeleted),
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task<CustomerDto?> GetByIdAsync(int userId, int id)
    {
        return await _context.Customers
            .Where(c => c.Id == id && c.UserId == userId && !c.IsDeleted)
            .Select(c => new CustomerDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Phone = c.Phone,
                PayPalCustomerId = c.PayPalCustomerId,
                InvoiceCount = c.Invoices.Count(i => !i.IsDeleted),
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<CustomerDto> CreateAsync(int userId, CreateCustomerRequest request)
    {
        var customer = new Customer
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone?.Trim()
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        return new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Phone = customer.Phone,
            PayPalCustomerId = customer.PayPalCustomerId,
            InvoiceCount = 0,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt
        };
    }

    public async Task<bool> UpdateAsync(int userId, int id, CreateCustomerRequest request)
    {
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && !c.IsDeleted);

        if (customer == null) return false;

        customer.Name = request.Name.Trim();
        customer.Email = request.Email.Trim();
        customer.Phone = request.Phone?.Trim();
        customer.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && !c.IsDeleted);

        if (customer == null) return false;

        // Invoices reference the customer, so removing the customer would orphan them.
        var hasInvoices = await _context.Invoices
            .AnyAsync(i => i.CustomerId == id && !i.IsDeleted);

        if (hasInvoices) return false;

        customer.IsDeleted = true;
        customer.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}
