using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ITransactionService : IBaseService<TransactionDTO>
    {
        Task<IEnumerable<TransactionDTO>> GetTransactionsByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<IEnumerable<TransactionDTO>> GetTransactionsByEmployeeAsync(string cashierId);
        Task<IEnumerable<TransactionDTO>> GetTransactionsByTypeAsync(TransactionType transactionType);
        Task<IEnumerable<TransactionDTO>> GetTransactionsByStatusAsync(TransactionStatus status);
        Task<IEnumerable<TransactionDTO>> SearchTransactionsAsync(string searchTerm);
        Task<TransactionDTO?> GetTransactionWithDetailsAsync(int transactionId);
        Task<decimal> GetTotalSalesAmountAsync(DateTime? startDate = null, DateTime? endDate = null);
        Task<decimal> GetEmployeeSalesAmountAsync(string cashierId, DateTime? startDate = null, DateTime? endDate = null);
        Task<bool> UpdateTransactionDiscountAsync(int transactionId, decimal newDiscount);
        Task<bool> UpdateTransactionDetailDiscountAsync(int transactionDetailId, decimal newDiscount);
        Task<bool> RemoveTransactionDetailAsync(int transactionDetailId);
        Task<bool> DeleteTransactionWithRestockAsync(int transactionId);
        Task<bool> UpdateTransactionDetailQuantityAsync(int transactionDetailId, decimal newQuantity);
        Task<IEnumerable<TransactionDTO>> GetFilteredTransactionsAsync(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? cashierId = null,
            TransactionType? transactionType = null,
            TransactionStatus? status = null,
            string? searchTerm = null);
        Task<Dictionary<string, decimal>> GetEmployeePerformanceAsync(DateTime? startDate = null, DateTime? endDate = null);
    }
}