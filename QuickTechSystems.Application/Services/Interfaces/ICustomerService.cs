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

        // CustomerPayment methods - NO repository exposure
        Task<IEnumerable<CustomerPaymentDTO>> GetCustomerPaymentsAsync(int customerId);
        Task<CustomerDTO> ProcessPaymentAsync(int customerId, decimal paymentAmount, string notes, string paymentMethod = "Cash");
        Task<CustomerPaymentDTO> UpdatePaymentAsync(CustomerPaymentDTO payment);
        Task<bool> DeletePaymentAsync(int paymentId, string reason);
    }
}