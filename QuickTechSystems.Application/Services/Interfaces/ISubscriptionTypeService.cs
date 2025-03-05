using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ISubscriptionTypeService : IBaseService<SubscriptionTypeDTO>
    {
        Task<IEnumerable<SubscriptionTypeDTO>> GetActiveTypesAsync();
        Task<SubscriptionTypeDTO?> GetByNameAsync(string name);
        Task<bool> IsNameUniqueAsync(string name, int? excludeId = null);
        Task<int> GetCustomerCountAsync(int typeId);
    }
}