using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuickTechSystems.Application.Services
{
    public class CustomerService : BaseService<Customer, CustomerDTO>, ICustomerService
    {
        private readonly IDrawerService _drawerService;
        private readonly Dictionary<string, IEnumerable<CustomerDTO>> _searchCache;
        private readonly HashSet<int> _activeOperations;
        private readonly Dictionary<int, decimal> _transactionBalanceCache;

        public CustomerService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService,
            IDrawerService drawerService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _drawerService = drawerService;
            _searchCache = new Dictionary<string, IEnumerable<CustomerDTO>>();
            _activeOperations = new HashSet<int>();
            _transactionBalanceCache = new Dictionary<int, decimal>();
        }

        public override async Task<IEnumerable<CustomerDTO>> GetAllAsync()
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var customers = await _repository.Query()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.Name)
                    .ToListAsync();

                var customerDtos = _mapper.Map<IEnumerable<CustomerDTO>>(customers).ToList();

                foreach (var dto in customerDtos)
                {
                    dto.TransactionCount = await _unitOfWork.Transactions
                        .Query()
                        .CountAsync(t => t.CustomerId == dto.CustomerId);
                }

                return customerDtos;
            }, "GetAllCustomers");
        }

        public async Task<IEnumerable<CustomerDTO>> SearchCustomersAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return await GetAllAsync();
            }

            var cacheKey = $"search_{searchTerm.ToLowerInvariant()}";
            if (_searchCache.ContainsKey(cacheKey))
            {
                return _searchCache[cacheKey];
            }

            return await ExecuteServiceOperationAsync(async () =>
            {
                var customers = await _repository.Query()
                    .Where(c => c.IsActive && (
                        c.Name.Contains(searchTerm) ||
                        c.Phone.Contains(searchTerm) ||
                        c.Email.Contains(searchTerm)
                    ))
                    .OrderBy(c => c.Name)
                    .ToListAsync();

                var customerDtos = _mapper.Map<IEnumerable<CustomerDTO>>(customers).ToList();

                foreach (var dto in customerDtos)
                {
                    dto.TransactionCount = await _unitOfWork.Transactions
                        .Query()
                        .CountAsync(t => t.CustomerId == dto.CustomerId);
                }

                _searchCache[cacheKey] = customerDtos;
                return customerDtos;
            }, "SearchCustomers");
        }

        public async Task<CustomerDTO> UpdateBalanceAsync(int customerId, decimal balanceAdjustment, string reason)
        {
            if (_activeOperations.Contains(customerId))
            {
                throw new InvalidOperationException("Balance update operation already in progress for this customer");
            }

            return await ExecuteServiceOperationAsync(async () =>
            {
                _activeOperations.Add(customerId);

                try
                {
                    var customer = await _repository.Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.CustomerId == customerId);

                    if (customer == null)
                    {
                        throw new ArgumentException($"Customer with ID {customerId} not found");
                    }

                    customer.Balance += balanceAdjustment;
                    customer.UpdatedAt = DateTime.Now;

                    _unitOfWork.DetachAllEntities();
                    await _repository.UpdateAsync(customer);
                    await _unitOfWork.SaveChangesAsync();

                    var updatedDto = _mapper.Map<CustomerDTO>(customer);
                    updatedDto.TransactionCount = await _unitOfWork.Transactions
                        .Query()
                        .AsNoTracking()
                        .CountAsync(t => t.CustomerId == customerId);

                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("BalanceUpdate", updatedDto));
                    ClearSearchCache();

                    return updatedDto;
                }
                finally
                {
                    _activeOperations.Remove(customerId);
                }
            }, "UpdateCustomerBalance");
        }

        public async Task<CustomerDTO> SetBalanceAsync(int customerId, decimal newBalance, string reason)
        {
            if (_activeOperations.Contains(customerId))
            {
                throw new InvalidOperationException("Balance update operation already in progress for this customer");
            }

            return await ExecuteServiceOperationAsync(async () =>
            {
                _activeOperations.Add(customerId);

                try
                {
                    var customer = await _repository.Query()
                        .FirstOrDefaultAsync(c => c.CustomerId == customerId);

                    if (customer == null)
                    {
                        throw new ArgumentException($"Customer with ID {customerId} not found");
                    }

                    customer.Balance = newBalance;
                    customer.UpdatedAt = DateTime.Now;

                    await _unitOfWork.SaveChangesAsync();

                    var updatedDto = _mapper.Map<CustomerDTO>(customer);
                    updatedDto.TransactionCount = await _unitOfWork.Transactions
                        .Query()
                        .AsNoTracking()
                        .CountAsync(t => t.CustomerId == customerId);

                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("BalanceSet", updatedDto));
                    ClearSearchCache();

                    return updatedDto;
                }
                finally
                {
                    _activeOperations.Remove(customerId);
                }
            }, "SetCustomerBalance");
        }

        public async Task<IEnumerable<TransactionDTO>> GetCustomerTransactionsAsync(int customerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var transactions = await _unitOfWork.Transactions
                    .Query()
                    .AsNoTracking()
                    .Where(t => t.CustomerId == customerId)
                    .OrderByDescending(t => t.TransactionDate)
                    .ToListAsync();

                var transactionDtos = _mapper.Map<IEnumerable<TransactionDTO>>(transactions).ToList();

                foreach (var dto in transactionDtos)
                {
                    dto.Details = new System.Collections.ObjectModel.ObservableCollection<TransactionDetailDTO>();

                    var details = await _unitOfWork.Context.Set<TransactionDetail>()
                        .AsNoTracking()
                        .Where(td => td.TransactionId == dto.TransactionId)
                        .ToListAsync();

                    foreach (var detail in details)
                    {
                        dto.Details.Add(_mapper.Map<TransactionDetailDTO>(detail));
                    }
                }

                return transactionDtos;
            }, "GetCustomerTransactions");
        }

        public async Task<CustomerDTO> ProcessPaymentAsync(int customerId, decimal paymentAmount, string notes)
        {
            if (_activeOperations.Contains(customerId))
            {
                throw new InvalidOperationException("Payment operation already in progress for this customer");
            }

            return await ExecuteServiceOperationAsync(async () =>
            {
                _activeOperations.Add(customerId);

                try
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();

                    var customer = await _repository.Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.CustomerId == customerId);

                    if (customer == null)
                    {
                        throw new ArgumentException($"Customer with ID {customerId} not found");
                    }

                    if (paymentAmount <= 0)
                    {
                        throw new ArgumentException("Payment amount must be greater than zero");
                    }

                    // Check if there's an open drawer
                    var currentDrawer = await _drawerService.GetCurrentDrawerAsync();
                    if (currentDrawer == null)
                    {
                        throw new InvalidOperationException("No open drawer found. Please open a drawer before processing payments.");
                    }

                    // Update customer balance
                    customer.Balance = Math.Max(0, customer.Balance - paymentAmount);
                    customer.UpdatedAt = DateTime.Now;

                    _unitOfWork.DetachAllEntities();
                    await _repository.UpdateAsync(customer);

                    // Create customer transaction record - ENSURE BOTH AMOUNTS ARE THE SAME
                    var paymentTransaction = new Transaction
                    {
                        CustomerId = customerId,
                        CustomerName = customer.Name,
                        TotalAmount = paymentAmount,      // Same amount
                        PaidAmount = paymentAmount,       // Same amount
                        TransactionDate = DateTime.Now,
                        TransactionType = TransactionType.Sale,
                        Status = TransactionStatus.Completed,
                        PaymentMethod = "Cash",
                        CashierId = "system",
                        CashierName = "System"
                    };

                    await _unitOfWork.Transactions.AddAsync(paymentTransaction);
                    await _unitOfWork.SaveChangesAsync(); // Save to get the transaction ID

                    // Create drawer transaction with direct reference to customer transaction
                    var description = string.IsNullOrWhiteSpace(notes)
                        ? $"Customer payment from {customer.Name}"
                        : $"Customer payment from {customer.Name}: {notes}";

                    var drawerTransaction = new DrawerTransaction
                    {
                        DrawerId = currentDrawer.DrawerId,
                        Timestamp = DateTime.Now,
                        Type = "Cash Receipt",
                        Amount = paymentAmount, // This matches PaidAmount
                        Balance = currentDrawer.CurrentBalance + paymentAmount,
                        Description = description,
                        ActionType = "Cash Receipt",
                        TransactionReference = $"CustomerPayment_{paymentTransaction.TransactionId}",
                        PaymentMethod = "Cash",
                        CustomerTransactionId = paymentTransaction.TransactionId
                    };

                    // Update drawer balance
                    var drawer = await _unitOfWork.Drawers.GetByIdAsync(currentDrawer.DrawerId);
                    if (drawer != null)
                    {
                        drawer.CurrentBalance += paymentAmount;
                        drawer.TotalSales += paymentAmount;
                        drawer.CashIn += paymentAmount;
                        drawer.LastUpdated = DateTime.Now;

                        await _unitOfWork.Drawers.UpdateAsync(drawer);
                        drawerTransaction.Balance = drawer.CurrentBalance;
                    }

                    await _unitOfWork.GetRepository<DrawerTransaction>().AddAsync(drawerTransaction);
                    await _unitOfWork.SaveChangesAsync();

                    await transaction.CommitAsync();

                    var updatedDto = _mapper.Map<CustomerDTO>(customer);
                    updatedDto.TransactionCount = await _unitOfWork.Transactions
                        .Query()
                        .AsNoTracking()
                        .CountAsync(t => t.CustomerId == customerId);

                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("PaymentProcessed", updatedDto));
                    _eventAggregator.Publish(new DrawerUpdateEvent("Customer Payment", paymentAmount, description));

                    ClearSearchCache();

                    return updatedDto;
                }
                finally
                {
                    _activeOperations.Remove(customerId);
                }
            }, "ProcessCustomerPayment");
        }
        public override async Task<CustomerDTO> CreateAsync(CustomerDTO dto)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var customer = _mapper.Map<Customer>(dto);
                customer.CreatedAt = DateTime.Now;
                customer.IsActive = true;

                var result = await _repository.AddAsync(customer);
                await _unitOfWork.SaveChangesAsync();

                var resultDto = _mapper.Map<CustomerDTO>(result);
                resultDto.TransactionCount = 0;

                _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Create", resultDto));
                ClearSearchCache();

                return resultDto;
            }, "CreateCustomer");
        }

        public override async Task UpdateAsync(CustomerDTO dto)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                var customer = _mapper.Map<Customer>(dto);
                customer.UpdatedAt = DateTime.Now;

                _unitOfWork.DetachAllEntities();
                await _repository.UpdateAsync(customer);
                await _unitOfWork.SaveChangesAsync();

                _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", dto));
                ClearSearchCache();
            }, "UpdateCustomer");
        }

        public override async Task DeleteAsync(int id)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                var customer = await _repository.Query()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CustomerId == id);

                if (customer == null) return;

                var hasTransactions = await _unitOfWork.Transactions
                    .Query()
                    .AsNoTracking()
                    .AnyAsync(t => t.CustomerId == id);

                if (hasTransactions)
                {
                    customer.IsActive = false;
                    customer.UpdatedAt = DateTime.Now;
                    _unitOfWork.DetachAllEntities();
                    await _repository.UpdateAsync(customer);
                }
                else
                {
                    _unitOfWork.DetachAllEntities();
                    await _repository.DeleteAsync(customer);
                }

                await _unitOfWork.SaveChangesAsync();

                var dto = _mapper.Map<CustomerDTO>(customer);
                _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Delete", dto));
                ClearSearchCache();
            }, "DeleteCustomer");
        }

        public async Task<TransactionDTO> UpdateTransactionAsync(TransactionDTO transaction)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                using var dbTransaction = await _unitOfWork.BeginTransactionAsync();

                var existingTransaction = await _unitOfWork.Transactions
                    .Query()
                    .FirstOrDefaultAsync(t => t.TransactionId == transaction.TransactionId);

                if (existingTransaction == null)
                {
                    throw new ArgumentException($"Transaction with ID {transaction.TransactionId} not found");
                }

                // Use PaidAmount for customer payments since that's what we set initially
                var originalAmount = existingTransaction.PaidAmount; // CHANGED: Use PaidAmount instead of TotalAmount
                var newAmount = transaction.TotalAmount;
                var amountDifference = newAmount - originalAmount;

                // Store the customer info before updating
                var customerId = existingTransaction.CustomerId;
                var customerName = existingTransaction.CustomerName;

                // Update BOTH TotalAmount and PaidAmount to keep them consistent
                existingTransaction.TotalAmount = transaction.TotalAmount;
                existingTransaction.PaidAmount = transaction.TotalAmount; // ADDED: Keep PaidAmount in sync
                existingTransaction.PaymentMethod = transaction.PaymentMethod;
                existingTransaction.Status = transaction.Status;
                existingTransaction.TransactionDate = transaction.TransactionDate;
                existingTransaction.TransactionType = transaction.TransactionType;

                // Update customer balance if there's a customer associated
                if (existingTransaction.CustomerId.HasValue)
                {
                    var customer = await _unitOfWork.Customers
                        .Query()
                        .FirstOrDefaultAsync(c => c.CustomerId == existingTransaction.CustomerId.Value);

                    if (customer != null)
                    {
                        if (existingTransaction.TransactionType == TransactionType.Sale)
                        {
                            customer.Balance -= amountDifference;
                        }
                        else
                        {
                            customer.Balance += amountDifference;
                        }

                        customer.UpdatedAt = DateTime.Now;
                    }
                }

                await _unitOfWork.SaveChangesAsync();

                // Update the drawer transaction using the direct reference
                if (Math.Abs(amountDifference) > 0.01m && existingTransaction.TransactionType == TransactionType.Sale)
                {
                    try
                    {
                        // Find the drawer transaction using the CustomerTransactionId
                        var drawerTransaction = await _unitOfWork.GetRepository<DrawerTransaction>()
                            .Query()
                            .FirstOrDefaultAsync(dt => dt.CustomerTransactionId == existingTransaction.TransactionId);

                        if (drawerTransaction != null)
                        {
                            // Update the drawer transaction amount
                            drawerTransaction.Amount = newAmount; // This should match the PaidAmount
                            drawerTransaction.Description = $"Customer payment from {customerName} - {transaction.PaymentMethod}";

                            await _unitOfWork.GetRepository<DrawerTransaction>().UpdateAsync(drawerTransaction);

                            // Update the drawer balance
                            var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerTransaction.DrawerId);
                            if (drawer != null)
                            {
                                drawer.CurrentBalance += amountDifference;
                                drawer.TotalSales += amountDifference;
                                drawer.CashIn += amountDifference;
                                drawer.LastUpdated = DateTime.Now;

                                await _unitOfWork.Drawers.UpdateAsync(drawer);

                                // Update the drawer transaction balance
                                drawerTransaction.Balance = drawer.CurrentBalance;
                                await _unitOfWork.GetRepository<DrawerTransaction>().UpdateAsync(drawerTransaction);
                            }

                            // Recalculate all subsequent drawer transaction balances
                            await RecalculateDrawerBalancesAfter(drawerTransaction.DrawerId, drawerTransaction.Timestamp);

                            // Publish drawer update event
                            _eventAggregator.Publish(new DrawerUpdateEvent(
                                "Payment Modified",
                                amountDifference,
                                $"Payment from {customerName} modified by {amountDifference:C}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to update drawer transaction: {ex.Message}");
                        throw; // Re-throw to ensure the transaction fails if drawer update fails
                    }
                }

                await dbTransaction.CommitAsync();

                var resultDto = _mapper.Map<TransactionDTO>(existingTransaction);
                _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>("Update", resultDto));
                ClearSearchCache();

                return resultDto;
            }, "UpdateTransaction");
        }

        private async Task RecalculateDrawerTransactionBalances(int drawerId)
        {
            var drawerTransactions = await _unitOfWork.GetRepository<DrawerTransaction>()
                .Query()
                .Where(dt => dt.DrawerId == drawerId)
                .OrderBy(dt => dt.Timestamp)
                .ToListAsync();

            decimal runningBalance = 0;
            foreach (var transaction in drawerTransactions)
            {
                if (transaction.Type.Equals("Open", StringComparison.OrdinalIgnoreCase))
                {
                    runningBalance = transaction.Amount;
                }
                else if (transaction.Type.Equals("Cash Receipt", StringComparison.OrdinalIgnoreCase) ||
                        transaction.Type.Equals("Cash In", StringComparison.OrdinalIgnoreCase) ||
                        transaction.Type.Equals("Cash Sale", StringComparison.OrdinalIgnoreCase))
                {
                    runningBalance += Math.Abs(transaction.Amount);
                }
                else
                {
                    runningBalance -= Math.Abs(transaction.Amount);
                }

                transaction.Balance = runningBalance;
                await _unitOfWork.GetRepository<DrawerTransaction>().UpdateAsync(transaction);
            }
        }
        public async Task<bool> DeleteTransactionAsync(int transactionId, string reason)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                using var dbTransaction = await _unitOfWork.BeginTransactionAsync();

                var transaction = await _unitOfWork.Transactions
                    .Query()
                    .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                if (transaction == null)
                {
                    return false;
                }

                var customerId = transaction.CustomerId;
                var customerName = transaction.CustomerName;
                var transactionAmount = transaction.PaidAmount; // CHANGED: Use PaidAmount
                var isPaymentTransaction = transaction.TransactionType == TransactionType.Sale;

                // Update customer balance if there's a customer associated
                if (transaction.CustomerId.HasValue)
                {
                    var customer = await _unitOfWork.Customers
                        .Query()
                        .FirstOrDefaultAsync(c => c.CustomerId == transaction.CustomerId.Value);

                    if (customer != null)
                    {
                        if (transaction.TransactionType == TransactionType.Sale)
                        {
                            customer.Balance += transaction.PaidAmount; // CHANGED: Use PaidAmount
                        }
                        else
                        {
                            customer.Balance -= transaction.PaidAmount; // CHANGED: Use PaidAmount
                        }

                        customer.UpdatedAt = DateTime.Now;
                    }
                }

                _unitOfWork.Context.Set<Transaction>().Remove(transaction);
                await _unitOfWork.SaveChangesAsync();

                // Remove the drawer transaction using the direct reference
                if (isPaymentTransaction)
                {
                    try
                    {
                        var drawerTransaction = await _unitOfWork.GetRepository<DrawerTransaction>()
                            .Query()
                            .FirstOrDefaultAsync(dt => dt.CustomerTransactionId == transactionId);

                        if (drawerTransaction != null)
                        {
                            var drawerId = drawerTransaction.DrawerId;
                            var timestamp = drawerTransaction.Timestamp;

                            // Update drawer balance
                            var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                            if (drawer != null)
                            {
                                drawer.CurrentBalance -= transactionAmount;
                                drawer.TotalSales -= transactionAmount;
                                drawer.CashIn -= transactionAmount;
                                drawer.LastUpdated = DateTime.Now;

                                await _unitOfWork.Drawers.UpdateAsync(drawer);
                            }

                            // Remove the drawer transaction
                            await _unitOfWork.GetRepository<DrawerTransaction>().DeleteAsync(drawerTransaction);

                            // Recalculate all subsequent drawer transaction balances
                            await RecalculateDrawerBalancesAfter(drawerId, timestamp);

                            // Publish drawer update event
                            _eventAggregator.Publish(new DrawerUpdateEvent(
                                "Payment Deletion",
                                -transactionAmount,
                                $"Deleted payment from {customerName}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to remove drawer transaction: {ex.Message}");
                        throw;
                    }
                }

                await dbTransaction.CommitAsync();

                var dto = _mapper.Map<TransactionDTO>(transaction);
                _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>("Delete", dto));
                ClearSearchCache();

                return true;
            }, "DeleteTransaction");
        }

        private async Task RecalculateDrawerBalancesAfter(int drawerId, DateTime afterTimestamp)
        {
            var subsequentTransactions = await _unitOfWork.GetRepository<DrawerTransaction>()
                .Query()
                .Where(dt => dt.DrawerId == drawerId && dt.Timestamp > afterTimestamp)
                .OrderBy(dt => dt.Timestamp)
                .ToListAsync();

            // Get the balance just before the timestamp
            var previousTransaction = await _unitOfWork.GetRepository<DrawerTransaction>()
                .Query()
                .Where(dt => dt.DrawerId == drawerId && dt.Timestamp <= afterTimestamp)
                .OrderByDescending(dt => dt.Timestamp)
                .FirstOrDefaultAsync();

            decimal runningBalance = previousTransaction?.Balance ?? 0;

            foreach (var transaction in subsequentTransactions)
            {
                if (transaction.Type.Equals("Cash Receipt", StringComparison.OrdinalIgnoreCase) ||
                    transaction.Type.Equals("Cash In", StringComparison.OrdinalIgnoreCase) ||
                    transaction.Type.Equals("Cash Sale", StringComparison.OrdinalIgnoreCase))
                {
                    runningBalance += Math.Abs(transaction.Amount);
                }
                else if (!transaction.Type.Equals("Open", StringComparison.OrdinalIgnoreCase))
                {
                    runningBalance -= Math.Abs(transaction.Amount);
                }

                transaction.Balance = runningBalance;
                await _unitOfWork.GetRepository<DrawerTransaction>().UpdateAsync(transaction);
            }
        }
        private void ClearSearchCache()
        {
            _searchCache.Clear();
            _transactionBalanceCache.Clear();
        }
    }
}