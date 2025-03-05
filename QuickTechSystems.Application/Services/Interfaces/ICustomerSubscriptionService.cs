using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ICustomerSubscriptionService
    {
        Task<IEnumerable<CustomerSubscriptionDTO>> GetAllAsync(); // Add this method
        Task<IEnumerable<CustomerSubscriptionDTO>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<IEnumerable<CustomerSubscriptionDTO>> GetByNameAsync(string searchText);
        Task<CustomerSubscriptionDTO> CreateAsync(CustomerSubscriptionDTO subscription);
        Task<CustomerSubscriptionDTO> UpdateAsync(CustomerSubscriptionDTO subscription);
        Task DeleteAsync(int id);
        Task<IEnumerable<CounterHistoryDTO>> GetCustomerHistoryAsync(int customerId);
        Task<IEnumerable<SubscriptionPaymentDTO>> GetCustomerPaymentsAsync(int customerId);
        Task<bool> UpdateCounterAsync(int customerId, decimal newCounter);
        Task<bool> ProcessPaymentAsync(SubscriptionPaymentDTO payment);

        Task<bool> SaveCounterReadingAsync(int customerId, CounterHistoryDTO reading);
    }
}