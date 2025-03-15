using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Interfaces;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace QuickTechSystems.Application.Services
{
    public class CustomerDebtService : ICustomerDebtService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IDrawerService _drawerService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDbContextScopeService _dbContextScopeService;

        public CustomerDebtService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IDrawerService drawerService,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _drawerService = drawerService ?? throw new ArgumentNullException(nameof(drawerService));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dbContextScopeService = dbContextScopeService ?? throw new ArgumentNullException(nameof(dbContextScopeService));
        }

        public async Task<IEnumerable<CustomerDTO>> GetCustomersWithDebtAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    Debug.WriteLine("Getting customers with debt");
                    var customers = await _unitOfWork.Customers.Query()
                        .Where(c => c.Balance > 0 && c.IsActive)
                        .OrderByDescending(c => c.Balance)
                        .ToListAsync();

                    Debug.WriteLine($"Found {customers.Count} customers with debt");
                    return _mapper.Map<IEnumerable<CustomerDTO>>(customers);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting customers with debt: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<IEnumerable<CustomerPaymentDTO>> GetPaymentHistoryAsync(int customerId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    Debug.WriteLine($"Getting payment history for customer {customerId}");
                    var payments = await _unitOfWork.Context.Set<CustomerPayment>()
                        .Include(p => p.Customer)
                        .Where(p => p.CustomerId == customerId)
                        .OrderByDescending(p => p.PaymentDate)
                        .ToListAsync();

                    Debug.WriteLine($"Found {payments.Count} payment records");
                    return _mapper.Map<IEnumerable<CustomerPaymentDTO>>(payments);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting payment history: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<bool> ProcessDebtPaymentAsync(int customerId, decimal amount, string paymentMethod)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    Debug.WriteLine($"Processing debt payment of {amount} for customer {customerId}");
                    var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
                    if (customer == null)
                    {
                        Debug.WriteLine("Customer not found");
                        return false;
                    }

                    if (amount > customer.Balance)
                    {
                        Debug.WriteLine($"Payment amount {amount} exceeds debt balance {customer.Balance}");
                        throw new InvalidOperationException("Payment amount cannot exceed debt balance");
                    }

                    if (paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine("Processing cash drawer transaction");
                        await _drawerService.ProcessDebtPaymentAsync(amount, customer.Name, $"Debt payment - {customer.CustomerId}");
                    }

                    // Record previous balance for event reporting
                    var previousBalance = customer.Balance;
                    customer.Balance -= amount;

                    // Create payment record
                    var payment = new CustomerPayment
                    {
                        CustomerId = customerId,
                        Customer = customer,
                        Amount = amount,
                        PaymentDate = DateTime.Now,
                        PaymentMethod = paymentMethod,
                        Notes = $"Debt payment - Previous Balance: {previousBalance:N}, New Balance: {customer.Balance:N}"
                    };

                    Debug.WriteLine("Adding payment record to database");
                    await _unitOfWork.Context.Set<CustomerPayment>().AddAsync(payment);

                    Debug.WriteLine("Updating customer");
                    await _unitOfWork.Customers.UpdateAsync(customer);

                    Debug.WriteLine("Saving changes");
                    await _unitOfWork.SaveChangesAsync();

                    await transaction.CommitAsync();
                    Debug.WriteLine("Transaction committed successfully");

                    // Publish events to update UI
                    var customerDto = _mapper.Map<CustomerDTO>(customer);
                    var paymentDto = _mapper.Map<CustomerPaymentDTO>(payment);

                    Debug.WriteLine("Publishing customer update event");
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", customerDto));

                    Debug.WriteLine("Publishing payment creation event");
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerPaymentDTO>("Create", paymentDto));

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing debt payment: {ex.Message}");
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        public async Task<bool> AddToDebtAsync(int customerId, decimal amount, string reference)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    Debug.WriteLine($"Adding debt of {amount} for customer {customerId}");
                    var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
                    if (customer == null)
                    {
                        Debug.WriteLine("Customer not found");
                        return false;
                    }

                    if (!await ValidateDebtLimitAsync(customerId, amount))
                    {
                        Debug.WriteLine("Debt limit would be exceeded");
                        throw new InvalidOperationException("This would exceed the customer's credit limit");
                    }

                    // Record previous balance for event reporting
                    var previousBalance = customer.Balance;
                    customer.Balance += amount;

                    // Create a payment record with negative amount to represent debt increase
                    var debtRecord = new CustomerPayment
                    {
                        CustomerId = customerId,
                        Customer = customer,
                        Amount = -amount, // Negative amount indicates debt increase
                        PaymentDate = DateTime.Now,
                        PaymentMethod = "Charge",
                        Notes = string.IsNullOrEmpty(reference)
                            ? $"Debt increase - Previous Balance: {previousBalance:N}, New Balance: {customer.Balance:N}"
                            : reference
                    };

                    Debug.WriteLine("Adding debt record to database");
                    await _unitOfWork.Context.Set<CustomerPayment>().AddAsync(debtRecord);

                    Debug.WriteLine("Updating customer");
                    await _unitOfWork.Customers.UpdateAsync(customer);

                    Debug.WriteLine("Saving changes");
                    await _unitOfWork.SaveChangesAsync();

                    await transaction.CommitAsync();
                    Debug.WriteLine("Transaction committed successfully");

                    // Publish events to update UI
                    var customerDto = _mapper.Map<CustomerDTO>(customer);
                    var debtRecordDto = _mapper.Map<CustomerPaymentDTO>(debtRecord);

                    Debug.WriteLine("Publishing customer update event");
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", customerDto));

                    Debug.WriteLine("Publishing debt record creation event");
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerPaymentDTO>("Create", debtRecordDto));

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error adding to debt: {ex.Message}");
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        public async Task<CustomerDTO> GetCustomerDebtDetailsAsync(int customerId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    Debug.WriteLine($"Getting debt details for customer {customerId}");
                    var customer = await _unitOfWork.Customers.Query()
                        .Include(c => c.Transactions)
                        .Include(c => c.Payments)
                        .FirstOrDefaultAsync(c => c.CustomerId == customerId);

                    if (customer == null)
                    {
                        Debug.WriteLine("Customer not found");
                        return null;
                    }

                    return _mapper.Map<CustomerDTO>(customer);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting customer debt details: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<decimal> GetTotalOutstandingDebtAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    Debug.WriteLine("Getting total outstanding debt");
                    var totalDebt = await _unitOfWork.Customers.Query()
                        .Where(c => c.IsActive)
                        .SumAsync(c => c.Balance);

                    Debug.WriteLine($"Total outstanding debt: {totalDebt}");
                    return totalDebt;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting total outstanding debt: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<bool> ValidateDebtLimitAsync(int customerId, decimal newDebtAmount)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    Debug.WriteLine($"Validating debt limit for customer {customerId}, new debt: {newDebtAmount}");
                    var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
                    if (customer == null)
                    {
                        Debug.WriteLine("Customer not found");
                        return false;
                    }

                    // If credit limit is 0, it means no limit is enforced
                    var result = customer.CreditLimit == 0 || (customer.Balance + newDebtAmount) <= customer.CreditLimit;

                    Debug.WriteLine($"Debt limit validation result: {result}. Current balance: {customer.Balance}, Credit limit: {customer.CreditLimit}");
                    return result;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error validating debt limit: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<IEnumerable<TransactionDTO>> GetDebtTransactionsAsync(int customerId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    Debug.WriteLine($"Getting debt transactions for customer {customerId}");
                    var transactions = await _unitOfWork.Transactions.Query()
                        .Include(t => t.Customer)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                        .Where(t => t.CustomerId == customerId && t.Balance > 0)
                        .OrderByDescending(t => t.TransactionDate)
                        .ToListAsync();

                    Debug.WriteLine($"Found {transactions.Count} debt transactions");
                    return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting debt transactions: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<bool> AdjustDebtBalanceAsync(int customerId, decimal adjustment, string reason)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    Debug.WriteLine($"Adjusting debt balance for customer {customerId} by {adjustment}");
                    var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
                    if (customer == null)
                    {
                        Debug.WriteLine("Customer not found");
                        return false;
                    }

                    // For negative adjustments (reducing debt), ensure it doesn't exceed the current balance
                    if (adjustment < 0 && Math.Abs(adjustment) > customer.Balance)
                    {
                        Debug.WriteLine($"Adjustment amount {adjustment} exceeds current balance {customer.Balance}");
                        throw new InvalidOperationException("Adjustment amount cannot exceed current balance");
                    }

                    // Record previous balance for event reporting
                    var previousBalance = customer.Balance;
                    customer.Balance += adjustment;

                    var adjustmentType = adjustment < 0 ? "Adjustment (Decrease)" : "Adjustment (Increase)";
                    var historyEntry = new CustomerPayment
                    {
                        CustomerId = customerId,
                        Customer = customer,
                        Amount = adjustment < 0 ? Math.Abs(adjustment) : -adjustment, // Positive for decrease, negative for increase
                        PaymentDate = DateTime.Now,
                        PaymentMethod = adjustmentType,
                        Notes = string.IsNullOrEmpty(reason)
                            ? $"Balance adjustment - Previous: {previousBalance:N}, New: {customer.Balance:N}"
                            : $"{reason} - Previous: {previousBalance:N}, New: {customer.Balance:N}"
                    };

                    Debug.WriteLine("Adding adjustment record to database");
                    await _unitOfWork.Context.Set<CustomerPayment>().AddAsync(historyEntry);

                    Debug.WriteLine("Updating customer");
                    await _unitOfWork.Customers.UpdateAsync(customer);

                    Debug.WriteLine("Saving changes");
                    await _unitOfWork.SaveChangesAsync();

                    await transaction.CommitAsync();
                    Debug.WriteLine("Transaction committed successfully");

                    // Publish events to update UI
                    var customerDto = _mapper.Map<CustomerDTO>(customer);
                    var historyEntryDto = _mapper.Map<CustomerPaymentDTO>(historyEntry);

                    Debug.WriteLine("Publishing customer update event");
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", customerDto));

                    Debug.WriteLine("Publishing adjustment creation event");
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerPaymentDTO>("Create", historyEntryDto));

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error adjusting debt balance: {ex.Message}");
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
    }
}