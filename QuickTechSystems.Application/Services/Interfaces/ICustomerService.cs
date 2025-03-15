// Application/Services/Interfaces/ICustomerService.cs
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ICustomerService : IBaseService<CustomerDTO>
    {
        Task<IEnumerable<CustomerDTO>> GetByNameAsync(string name);
        Task<IEnumerable<CustomerDTO>> GetCustomersWithDebtAsync();
        Task<IEnumerable<CustomerPaymentDTO>> GetPaymentHistoryAsync(int customerId);
        Task ProcessPaymentAsync(int customerId, decimal amount);
        Task AddToBalanceAsync(int customerId, decimal amount);
        Task ProcessPaymentAsync(CustomerPaymentDTO payment);
        Task SetCustomProductPricesAsync(int customerId, IEnumerable<CustomerProductPriceDTO> prices);
        Task<IEnumerable<CustomerProductPriceDTO>> GetCustomProductPricesAsync(int customerId);
    }
}