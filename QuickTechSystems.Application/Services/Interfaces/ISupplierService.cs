using Microsoft.EntityFrameworkCore.Storage;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ISupplierService : IBaseService<SupplierDTO>
    {
        Task<IEnumerable<SupplierDTO>> GetByNameAsync(string name);
        Task<bool> UpdateBalanceAsync(int supplierId, decimal amount, IDbContextTransaction? existingTransaction = null);
        Task<IEnumerable<SupplierDTO>> GetWithOutstandingBalanceAsync();
        Task<IEnumerable<SupplierTransactionDTO>> GetSupplierTransactionsAsync(int supplierId);
        Task<SupplierTransactionDTO> AddTransactionAsync(SupplierTransactionDTO transaction);
    }
}