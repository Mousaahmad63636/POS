// QuickTechSystems.Application.Services/CustomerService.cs
using System.Diagnostics;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Interfaces;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class CustomerService : BaseService<Customer, CustomerDTO>, ICustomerService
    {
        private readonly IDrawerService _drawerService;

        public CustomerService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService,
            IDrawerService drawerService = null) // Optional to allow for DI resolution
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _drawerService = drawerService;
        }

        public async Task<IEnumerable<CustomerDTO>> GetByNameAsync(string name)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var customers = await _repository.Query()
                    .Where(c => c.Name.Contains(name))
                    .ToListAsync();
                return _mapper.Map<IEnumerable<CustomerDTO>>(customers);
            });
        }

        public new async Task<CustomerDTO?> GetByIdAsync(int id)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    var customer = await _repository.Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.CustomerId == id);
                    return _mapper.Map<CustomerDTO>(customer);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting customer: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<IEnumerable<CustomerProductPriceDTO>> GetCustomProductPricesAsync(int customerId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var customerPrices = await _unitOfWork.Context.Set<CustomerProductPrice>()
                    .Include(cpp => cpp.Product)
                    .Where(cpp => cpp.CustomerId == customerId)
                    .Select(cpp => new CustomerProductPriceDTO
                    {
                        CustomerProductPriceId = cpp.CustomerProductPriceId,
                        CustomerId = cpp.CustomerId,
                        ProductId = cpp.ProductId,
                        Price = cpp.Price,
                        ProductName = cpp.Product.Name,
                        DefaultPrice = cpp.Product.SalePrice
                    })
                    .ToListAsync();

                return customerPrices;
            });
        }

        public async Task SetCustomProductPricesAsync(int customerId, IEnumerable<CustomerProductPriceDTO> prices)
        {
            await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var customerPricesSet = _unitOfWork.Context.Set<CustomerProductPrice>();

                // Remove existing prices for this customer
                var existingPrices = await customerPricesSet
                    .Where(cpp => cpp.CustomerId == customerId)
                    .ToListAsync();
                customerPricesSet.RemoveRange(existingPrices);

                // Add new prices
                var newPrices = prices.Select(p => new CustomerProductPrice
                {
                    CustomerId = customerId,
                    ProductId = p.ProductId,
                    Price = p.Price
                });

                await customerPricesSet.AddRangeAsync(newPrices);
                await _unitOfWork.SaveChangesAsync();

                // Publish event to notify of customer update
                var customer = await _repository.GetByIdAsync(customerId);
                if (customer != null)
                {
                    var customerDto = _mapper.Map<CustomerDTO>(customer);
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", customerDto));
                }
            });
        }

        // New methods for debt management
        public async Task<bool> UpdateBalanceAsync(int customerId, decimal amount)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    var customer = await _repository.GetByIdAsync(customerId);
                    if (customer == null)
                        return false;

                    // Update customer balance
                    customer.Balance += amount;
                    customer.UpdatedAt = DateTime.Now;

                    await _repository.UpdateAsync(customer);
                    await _unitOfWork.SaveChangesAsync();

                    // Publish customer updated event
                    var customerDto = _mapper.Map<CustomerDTO>(customer);
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", customerDto));

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating customer balance: {ex.Message}");
                    return false;
                }
            });
        }
        // Path: QuickTechSystems.Application/Services/CustomerService.cs
        public async Task<bool> ProcessPaymentAsync(int customerId, decimal amount, string reference)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var customer = await _repository.GetByIdAsync(customerId);
                    if (customer == null)
                        return false;

                    if (amount <= 0)
                        throw new InvalidOperationException("Payment amount must be greater than zero");

                    if (amount > customer.Balance)
                        throw new InvalidOperationException("Payment amount cannot exceed customer balance");

                    // Update customer balance (reduce debt)
                    customer.Balance -= amount;
                    customer.UpdatedAt = DateTime.Now;

                    await _repository.UpdateAsync(customer);
                    await _unitOfWork.SaveChangesAsync();

                    // Create a payment transaction record
                    var paymentTransaction = new Transaction
                    {
                        CustomerId = customerId,
                        CustomerName = customer.Name,
                        TotalAmount = amount,
                        PaidAmount = amount, // Set PaidAmount explicitly for payment transactions
                        TransactionDate = DateTime.Now,
                        TransactionType = Domain.Enums.TransactionType.Adjustment, // Changed from Payment to Adjustment
                        Status = Domain.Enums.TransactionStatus.Completed,
                        PaymentMethod = "Cash",
                        CashierId = "System",
                        CashierName = "Debt Payment"
                    };

                    await _unitOfWork.Transactions.AddAsync(paymentTransaction);
                    await _unitOfWork.SaveChangesAsync();

                    // Update drawer (increase cash) if the drawer service is available
                    if (_drawerService != null)
                    {
                        await _drawerService.ProcessCashReceiptAsync(
                            amount,
                            $"Debt payment from: {customer.Name}, Ref: {reference}"
                        );
                    }

                    await transaction.CommitAsync();

                    // Publish customer updated event
                    var customerDto = _mapper.Map<CustomerDTO>(customer);
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", customerDto));

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing customer payment: {ex.Message}");
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        public async Task<decimal> GetBalanceAsync(int customerId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var customer = await _repository.GetByIdAsync(customerId);
                return customer?.Balance ?? 0;
            });
        }
    }
}