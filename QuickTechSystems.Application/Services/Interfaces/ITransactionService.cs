using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Domain.Enums;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ITransactionService : IBaseService<TransactionDTO>
    {
        Task<IEnumerable<TransactionDTO>> GetByCustomerAsync(int customerId);
        Task<IEnumerable<TransactionDTO>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<IEnumerable<TransactionDTO>> GetByTypeAsync(TransactionType type);
        Task<bool> UpdateStatusAsync(int id, TransactionStatus status);
        Task<decimal> GetTotalSalesAsync(DateTime startDate, DateTime endDate);
        Task<TransactionDTO> ProcessSaleAsync(TransactionDTO transaction);
        Task<TransactionDTO?> GetLastTransactionAsync();
        Task<int> GetLatestTransactionIdAsync();
        // Path: QuickTechSystems.Application/Services/Interfaces/ITransactionService.cs

        Task<IEnumerable<TransactionDTO>> GetByCustomerAndDateRangeAsync(int customerId, DateTime startDate, DateTime endDate);
        Task<bool> DeleteAsync(int transactionId);
        Task<TransactionDTO> ProcessPaymentTransactionAsync(TransactionDTO transaction);
        Task<int> GetTransactionCountByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<TransactionDTO> UpdateAsync(TransactionDTO transaction);
        Task<decimal> GetTransactionSummaryByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<Dictionary<string, decimal>> GetCategorySalesByDateRangeAsync(
            DateTime startDate, DateTime endDate);
        Task<TransactionDTO> ProcessSaleAsync(TransactionDTO transaction, int cashierId);
    }
}