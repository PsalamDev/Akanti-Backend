using Akanti.API.DTOs.Invoice;

namespace Akanti.API.Interfaces;

public interface ICustomerService
{
    Task<List<CustomerDto>> GetAllAsync(int userId, string? search);
    Task<CustomerDto?> GetByIdAsync(int userId, int id);
    Task<CustomerDto> CreateAsync(int userId, CreateCustomerRequest request);
    Task<bool> UpdateAsync(int userId, int id, CreateCustomerRequest request);
    Task<bool> DeleteAsync(int userId, int id);
}
