// Application/Services/Interfaces/ICustomerService.cs
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ICustomerService : IBaseService<CustomerDTO>
    {
        Task<IEnumerable<CustomerDTO>> GetByNameAsync(string name);
        Task SetCustomProductPricesAsync(int customerId, IEnumerable<CustomerProductPriceDTO> prices);
        Task<IEnumerable<CustomerProductPriceDTO>> GetCustomProductPricesAsync(int customerId);
    }
}