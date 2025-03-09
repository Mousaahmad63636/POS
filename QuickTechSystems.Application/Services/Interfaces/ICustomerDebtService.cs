using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ICustomerDebtService
    {
        Task<IEnumerable<CustomerDTO>> GetCustomersWithDebtAsync();
        Task<IEnumerable<CustomerPaymentDTO>> GetPaymentHistoryAsync(int customerId);
        Task<bool> ProcessDebtPaymentAsync(int customerId, decimal amount, string paymentMethod);
        Task<bool> AddToDebtAsync(int customerId, decimal amount, string reference);
        Task<CustomerDTO> GetCustomerDebtDetailsAsync(int customerId);
        Task<decimal> GetTotalOutstandingDebtAsync();
        Task<bool> ValidateDebtLimitAsync(int customerId, decimal newDebtAmount);
        Task<IEnumerable<TransactionDTO>> GetDebtTransactionsAsync(int customerId);
        Task<bool> AdjustDebtBalanceAsync(int customerId, decimal adjustment, string reason);
    }
}