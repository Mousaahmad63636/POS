using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Interfaces;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;
using QuickTechSystems.Domain.Enums;

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
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _drawerService = drawerService;
            _eventAggregator = eventAggregator;
            _dbContextScopeService = dbContextScopeService;
        }

        public async Task<IEnumerable<CustomerDTO>> GetCustomersWithDebtAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var customers = await _unitOfWork.Customers.Query()
                    .Where(c => c.Balance > 0)
                    .OrderByDescending(c => c.Balance)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<CustomerDTO>>(customers);
            });
        }

        public async Task<IEnumerable<CustomerPaymentDTO>> GetPaymentHistoryAsync(int customerId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var payments = await _unitOfWork.Context.Set<CustomerPayment>()
                    .Include(p => p.Customer)
                    .Where(p => p.CustomerId == customerId)
                    .OrderByDescending(p => p.PaymentDate)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<CustomerPaymentDTO>>(payments);
            });
        }

        public async Task<bool> ProcessDebtPaymentAsync(int customerId, decimal amount, string paymentMethod)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
                    if (customer == null) return false;

                    if (amount > customer.Balance)
                        throw new InvalidOperationException("Payment amount cannot exceed debt balance");

                    if (paymentMethod == "Cash")
                    {
                        await _drawerService.ProcessDebtPaymentAsync(amount, customer.Name, $"Debt payment - {customer.CustomerId}");
                    }

                    customer.Balance -= amount;

                    // Create payment record
                    var payment = new CustomerPayment
                    {
                        CustomerId = customerId,
                        Customer = customer,  // Set the required Customer property
                        Amount = amount,
                        PaymentDate = DateTime.Now,
                        PaymentMethod = paymentMethod,
                        Notes = $"Debt payment - Balance: {customer.Balance:C2}"
                    };

                    await _unitOfWork.Context.Set<CustomerPayment>().AddAsync(payment);
                    await _unitOfWork.Customers.UpdateAsync(customer);
                    await _unitOfWork.SaveChangesAsync();

                    await transaction.CommitAsync();

                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>(
                        "Update",
                        _mapper.Map<CustomerDTO>(customer)
                    ));

                    return true;
                }
                catch
                {
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
                    var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
                    if (customer == null) return false;

                    if (!await ValidateDebtLimitAsync(customerId, amount))
                        throw new InvalidOperationException("This would exceed the customer's credit limit");

                    customer.Balance += amount;

                    await _unitOfWork.Customers.UpdateAsync(customer);
                    await _unitOfWork.SaveChangesAsync();

                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>(
                        "Update",
                        _mapper.Map<CustomerDTO>(customer)
                    ));

                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        public async Task<CustomerDTO> GetCustomerDebtDetailsAsync(int customerId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var customer = await _unitOfWork.Customers.Query()
                    .Include(c => c.Transactions)
                    .FirstOrDefaultAsync(c => c.CustomerId == customerId);

                return _mapper.Map<CustomerDTO>(customer);
            });
        }

        public async Task<decimal> GetTotalOutstandingDebtAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                return await _unitOfWork.Customers.Query()
                    .SumAsync(c => c.Balance);
            });
        }

        public async Task<bool> ValidateDebtLimitAsync(int customerId, decimal newDebtAmount)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
                if (customer == null) return false;

                return customer.CreditLimit == 0 || // 0 means no limit
                       (customer.Balance + newDebtAmount) <= customer.CreditLimit;
            });
        }

        public async Task<IEnumerable<TransactionDTO>> GetDebtTransactionsAsync(int customerId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var transactions = await _unitOfWork.Transactions.Query()
                    .Include(t => t.Customer)
                    .Include(t => t.TransactionDetails)
                    .Where(t => t.CustomerId == customerId && t.Balance > 0)
                    .OrderByDescending(t => t.TransactionDate)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
            });
        }

        public async Task<bool> AdjustDebtBalanceAsync(int customerId, decimal adjustment, string reason)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
                    if (customer == null) return false;

                    customer.Balance += adjustment;

                    var historyEntry = new CustomerPayment
                    {
                        CustomerId = customerId,
                        Customer = customer,  // Set the required Customer property
                        Amount = Math.Abs(adjustment),
                        PaymentDate = DateTime.Now,
                        PaymentMethod = adjustment < 0 ? "Adjustment (Decrease)" : "Adjustment (Increase)",
                        Notes = reason
                    };

                    await _unitOfWork.Context.Set<CustomerPayment>().AddAsync(historyEntry);
                    await _unitOfWork.Customers.UpdateAsync(customer);
                    await _unitOfWork.SaveChangesAsync();

                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>(
                        "Update",
                        _mapper.Map<CustomerDTO>(customer)
                    ));

                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
    }
}