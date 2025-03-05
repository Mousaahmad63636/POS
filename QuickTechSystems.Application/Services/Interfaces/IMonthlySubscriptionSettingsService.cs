using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface IMonthlySubscriptionSettingsService
    {
        Task<MonthlySubscriptionSettingsDTO> GetSettingsAsync();
        Task<MonthlySubscriptionSettingsDTO> UpdateSettingsAsync(MonthlySubscriptionSettingsDTO settings);
        Task<decimal> GetDefaultUnitPriceAsync();
        Task UpdateDefaultUnitPriceAsync(decimal newPrice, string modifiedBy);
        Task UpdateAllCustomerPricesAsync(decimal newPrice, string modifiedBy);
    }
}