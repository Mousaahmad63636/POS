using QuickTechSystems.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ICustomerService : IBaseService<CustomerDTO>
    {
        Task<IEnumerable<CustomerDTO>> SearchCustomersAsync(string searchTerm);
        Task<CustomerDTO> UpdateBalanceAsync(int customerId, decimal balanceAdjustment, string reason);
        Task<CustomerDTO> SetBalanceAsync(int customerId, decimal newBalance, string reason);
        Task<IEnumerable<TransactionDTO>> GetCustomerTransactionsAsync(int customerId);
        Task<CustomerDTO> ProcessPaymentAsync(int customerId, decimal paymentAmount, string notes);
        Task<TransactionDTO> UpdateTransactionAsync(TransactionDTO transaction);
        Task<bool> DeleteTransactionAsync(int transactionId, string reason);
    }
}