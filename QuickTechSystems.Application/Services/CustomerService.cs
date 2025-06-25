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

                // Update transaction count to use CustomerPayments instead of Transactions
                foreach (var dto in customerDtos)
                {
                    dto.TransactionCount = await _unitOfWork.CustomerPayments
                        .Query()
                        .CountAsync(cp => cp.CustomerId == dto.CustomerId);
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
                    dto.TransactionCount = await _unitOfWork.CustomerPayments
                        .Query()
                        .CountAsync(cp => cp.CustomerId == dto.CustomerId);
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
                    updatedDto.TransactionCount = await _unitOfWork.CustomerPayments
                        .Query()
                        .AsNoTracking()
                        .CountAsync(cp => cp.CustomerId == customerId);

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
                    updatedDto.TransactionCount = await _unitOfWork.CustomerPayments
                        .Query()
                        .AsNoTracking()
                        .CountAsync(cp => cp.CustomerId == customerId);

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

        public async Task<IEnumerable<CustomerPaymentDTO>> GetCustomerPaymentsAsync(int customerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var payments = await _unitOfWork.CustomerPayments
                    .Query()
                    .AsNoTracking()
                    .Include(cp => cp.Customer)
                    .Where(cp => cp.CustomerId == customerId)
                    .OrderByDescending(cp => cp.PaymentDate)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<CustomerPaymentDTO>>(payments);
            }, "GetCustomerPayments");
        }

        public async Task<CustomerDTO> ProcessPaymentAsync(int customerId, decimal paymentAmount, string notes, string paymentMethod = "Cash")
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

                    await _repository.UpdateAsync(customer);

                    // Update drawer balance first
                    var drawer = await _unitOfWork.Drawers.GetByIdAsync(currentDrawer.DrawerId);
                    if (drawer != null)
                    {
                        drawer.CurrentBalance += paymentAmount;
                        drawer.TotalSales += paymentAmount;
                        drawer.CashIn += paymentAmount;
                        drawer.LastUpdated = DateTime.Now;

                        await _unitOfWork.Drawers.UpdateAsync(drawer);
                    }

                    // Create drawer transaction with correct balance
                    var description = string.IsNullOrWhiteSpace(notes)
                        ? $"Customer payment from {customer.Name}"
                        : $"Customer payment from {customer.Name}: {notes}";

                    var drawerTransaction = new DrawerTransaction
                    {
                        DrawerId = currentDrawer.DrawerId,
                        Timestamp = DateTime.Now,
                        Type = "Cash Receipt",
                        Amount = paymentAmount,
                        Balance = drawer?.CurrentBalance ?? (currentDrawer.CurrentBalance + paymentAmount),
                        Description = description,
                        ActionType = "Cash Receipt",
                        TransactionReference = $"CustomerPayment_{customerId}_{DateTime.Now:yyyyMMddHHmmss}",
                        PaymentMethod = paymentMethod
                    };

                    await _unitOfWork.GetRepository<DrawerTransaction>().AddAsync(drawerTransaction);

                    // Save changes to get the DrawerTransaction ID
                    await _unitOfWork.SaveChangesAsync();

                    // Create customer payment record
                    var customerPayment = new CustomerPayment
                    {
                        CustomerId = customerId,
                        DrawerTransactionId = drawerTransaction.TransactionId,
                        Amount = paymentAmount,
                        PaymentDate = DateTime.Now,
                        PaymentMethod = paymentMethod,
                        Notes = notes,
                        Status = "Completed",
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System" // You can pass actual user info here
                    };

                    await _unitOfWork.CustomerPayments.AddAsync(customerPayment);
                    await _unitOfWork.SaveChangesAsync();

                    await transaction.CommitAsync();

                    var updatedDto = _mapper.Map<CustomerDTO>(customer);
                    updatedDto.TransactionCount = await _unitOfWork.CustomerPayments
                        .Query()
                        .AsNoTracking()
                        .CountAsync(cp => cp.CustomerId == customerId);

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

        public async Task<CustomerPaymentDTO> UpdatePaymentAsync(CustomerPaymentDTO paymentDto)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                using var dbTransaction = await _unitOfWork.BeginTransactionAsync();

                var existingPayment = await _unitOfWork.CustomerPayments
                    .Query()
                    .Include(cp => cp.Customer)
                    .FirstOrDefaultAsync(cp => cp.PaymentId == paymentDto.PaymentId);

                if (existingPayment == null)
                {
                    throw new ArgumentException($"Payment with ID {paymentDto.PaymentId} not found");
                }

                var originalAmount = existingPayment.Amount;
                var newAmount = paymentDto.Amount;
                var amountDifference = newAmount - originalAmount;
                var customerId = existingPayment.CustomerId;

                // Update payment record
                existingPayment.Amount = paymentDto.Amount;
                existingPayment.PaymentMethod = paymentDto.PaymentMethod;
                existingPayment.Notes = paymentDto.Notes;
                existingPayment.PaymentDate = paymentDto.PaymentDate;
                existingPayment.UpdatedAt = DateTime.Now;

                await _unitOfWork.CustomerPayments.UpdateAsync(existingPayment);

                // Update customer balance
                var customer = await _unitOfWork.Customers
                    .Query()
                    .FirstOrDefaultAsync(c => c.CustomerId == customerId);

                if (customer != null)
                {
                    customer.Balance -= amountDifference; // Subtract the difference (if amount increased, balance decreases more)
                    customer.Balance = Math.Max(0, customer.Balance);
                    customer.UpdatedAt = DateTime.Now;
                    await _unitOfWork.Customers.UpdateAsync(customer);
                }

                // Update linked drawer transaction
                if (existingPayment.DrawerTransactionId.HasValue && Math.Abs(amountDifference) > 0.01m)
                {
                    var drawerTransaction = await _unitOfWork.GetRepository<DrawerTransaction>()
                        .Query()
                        .FirstOrDefaultAsync(dt => dt.TransactionId == existingPayment.DrawerTransactionId.Value);

                    if (drawerTransaction != null)
                    {
                        drawerTransaction.Amount = newAmount;
                        drawerTransaction.Description = string.IsNullOrWhiteSpace(paymentDto.Notes)
                            ? $"Customer payment from {existingPayment.Customer?.Name}"
                            : $"Customer payment from {existingPayment.Customer?.Name}: {paymentDto.Notes}";
                        drawerTransaction.PaymentMethod = paymentDto.PaymentMethod;

                        await _unitOfWork.GetRepository<DrawerTransaction>().UpdateAsync(drawerTransaction);

                        // Update drawer balance
                        var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerTransaction.DrawerId);
                        if (drawer != null)
                        {
                            drawer.CurrentBalance += amountDifference;
                            drawer.TotalSales += amountDifference;
                            drawer.CashIn += amountDifference;
                            drawer.LastUpdated = DateTime.Now;

                            await _unitOfWork.Drawers.UpdateAsync(drawer);

                            // Update drawer transaction balance
                            drawerTransaction.Balance = drawer.CurrentBalance;
                            await _unitOfWork.GetRepository<DrawerTransaction>().UpdateAsync(drawerTransaction);
                        }

                        // Recalculate subsequent drawer transaction balances
                        await RecalculateDrawerBalancesAfter(drawerTransaction.DrawerId, drawerTransaction.Timestamp);
                    }
                }

                await _unitOfWork.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                var resultDto = _mapper.Map<CustomerPaymentDTO>(existingPayment);
                _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("PaymentUpdated",
                    _mapper.Map<CustomerDTO>(customer)));

                if (Math.Abs(amountDifference) > 0.01m)
                {
                    _eventAggregator.Publish(new DrawerUpdateEvent(
                        "Payment Modified",
                        amountDifference,
                        $"Payment from {existingPayment.Customer?.Name} modified by {amountDifference:C}"));
                }

                ClearSearchCache();
                return resultDto;
            }, "UpdatePayment");
        }

        public async Task<bool> DeletePaymentAsync(int paymentId, string reason)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                using var dbTransaction = await _unitOfWork.BeginTransactionAsync();

                var payment = await _unitOfWork.CustomerPayments
                    .Query()
                    .Include(cp => cp.Customer)
                    .FirstOrDefaultAsync(cp => cp.PaymentId == paymentId);

                if (payment == null)
                {
                    return false;
                }

                var customerId = payment.CustomerId;
                var paymentAmount = payment.Amount;
                var customerName = payment.Customer?.Name;

                // Update customer balance (add back the payment amount)
                var customer = await _unitOfWork.Customers
                    .Query()
                    .FirstOrDefaultAsync(c => c.CustomerId == customerId);

                if (customer != null)
                {
                    customer.Balance += paymentAmount;
                    customer.UpdatedAt = DateTime.Now;
                    await _unitOfWork.Customers.UpdateAsync(customer);
                }

                // Remove linked drawer transaction
                if (payment.DrawerTransactionId.HasValue)
                {
                    var drawerTransaction = await _unitOfWork.GetRepository<DrawerTransaction>()
                        .Query()
                        .FirstOrDefaultAsync(dt => dt.TransactionId == payment.DrawerTransactionId.Value);

                    if (drawerTransaction != null)
                    {
                        var drawerId = drawerTransaction.DrawerId;
                        var timestamp = drawerTransaction.Timestamp;

                        // Update drawer balance
                        var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                        if (drawer != null)
                        {
                            drawer.CurrentBalance -= paymentAmount;
                            drawer.TotalSales -= paymentAmount;
                            drawer.CashIn -= paymentAmount;
                            drawer.LastUpdated = DateTime.Now;

                            await _unitOfWork.Drawers.UpdateAsync(drawer);
                        }

                        // Remove the drawer transaction
                        await _unitOfWork.GetRepository<DrawerTransaction>().DeleteAsync(drawerTransaction);

                        // Recalculate subsequent drawer transaction balances
                        await RecalculateDrawerBalancesAfter(drawerId, timestamp);
                    }
                }

                // Remove the payment record
                await _unitOfWork.CustomerPayments.DeleteAsync(payment);
                await _unitOfWork.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                var customerDto = _mapper.Map<CustomerDTO>(customer);
                _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("PaymentDeleted", customerDto));
                _eventAggregator.Publish(new DrawerUpdateEvent(
                    "Payment Deletion",
                    -paymentAmount,
                    $"Deleted payment from {customerName}"));

                ClearSearchCache();
                return true;
            }, "DeletePayment");
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

                var hasPayments = await _unitOfWork.CustomerPayments
                    .Query()
                    .AsNoTracking()
                    .AnyAsync(cp => cp.CustomerId == id);

                if (hasPayments)
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
        }
    }
}