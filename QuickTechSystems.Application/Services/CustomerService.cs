// QuickTechSystems.Application/Services/CustomerService.cs
using System;
using System.Diagnostics;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class CustomerService : BaseService<Customer, CustomerDTO>, ICustomerService
    {
        public CustomerService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator) : base(unitOfWork, mapper, unitOfWork.Customers, eventAggregator)
        {
        }

        public async Task<IEnumerable<CustomerDTO>> GetByNameAsync(string name)
        {
            var customers = await _repository.Query()
                .Where(c => c.Name.Contains(name))
                .ToListAsync();
            return _mapper.Map<IEnumerable<CustomerDTO>>(customers);
        }

        public async Task<IEnumerable<CustomerDTO>> GetCustomersWithDebtAsync()
        {
            var customers = await _repository.Query()
                .Where(c => c.Balance > 0)
                .ToListAsync();
            return _mapper.Map<IEnumerable<CustomerDTO>>(customers);
        }

        public async Task<IEnumerable<CustomerPaymentDTO>> GetPaymentHistoryAsync(int customerId)
        {
            var payments = await _unitOfWork.Context.Set<CustomerPayment>()
                .Include(p => p.Customer)
                .Where(p => p.CustomerId == customerId)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
            return _mapper.Map<IEnumerable<CustomerPaymentDTO>>(payments);
        }

        public async Task ProcessPaymentAsync(int customerId, decimal amount)
        {
            var customer = await _repository.GetByIdAsync(customerId);
            if (customer == null)
                throw new InvalidOperationException("Customer not found");

            customer.Balance -= amount;
            var payment = new CustomerPayment
            {
                CustomerId = customerId,
                Amount = amount,
                PaymentDate = DateTime.Now,
                PaymentMethod = "Cash",
                Customer = customer
            };

            await _unitOfWork.Context.Set<CustomerPayment>().AddAsync(payment);
            await _repository.UpdateAsync(customer);
        }

        public async Task AddToBalanceAsync(int customerId, decimal amount)
        {
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var customer = await _repository.GetByIdAsync(customerId);
                if (customer == null)
                    throw new InvalidOperationException("Customer not found");

                customer.Balance += amount;
                await _repository.UpdateAsync(customer);
                await _unitOfWork.SaveChangesAsync();

                // Publish customer update event
                var customerDto = _mapper.Map<CustomerDTO>(customer);
                _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", customerDto));

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task ProcessPaymentAsync(CustomerPaymentDTO payment)
        {
            var customerPayment = _mapper.Map<CustomerPayment>(payment);
            await _unitOfWork.Context.Set<CustomerPayment>().AddAsync(customerPayment);
            await _unitOfWork.SaveChangesAsync();
        }

        // Override the DeleteAsync method to implement safer deletion with dependency checking
        public override async Task DeleteAsync(int id)
        {
            Debug.WriteLine($"Starting DeleteAsync for customer {id}");

            // Begin transaction for safety
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var customer = await _repository.Query()
                    .Include(c => c.Transactions)
                    .Include(c => c.Payments)
                    .FirstOrDefaultAsync(c => c.CustomerId == id);

                if (customer == null)
                {
                    Debug.WriteLine($"Customer with ID {id} not found");
                    throw new InvalidOperationException($"Customer with ID {id} not found");
                }

                // Check if there are any related records that would prevent deletion
                bool hasTransactions = customer.Transactions.Any();
                bool hasPayments = customer.Payments.Any();

                Debug.WriteLine($"Customer has transactions: {hasTransactions}, has payments: {hasPayments}");

                if (hasTransactions || hasPayments)
                {
                    // If there are related records, we can't physically delete the customer
                    // Perform soft delete instead
                    Debug.WriteLine("Performing soft delete due to related records");
                    customer.IsActive = false;

                    await _repository.UpdateAsync(customer);
                    await _unitOfWork.SaveChangesAsync();

                    var dto = _mapper.Map<CustomerDTO>(customer);
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", dto));

                    await transaction.CommitAsync();

                    // Throw exception to indicate soft delete was performed
                    throw new InvalidOperationException(
                        "This customer has associated records and cannot be physically deleted. " +
                        "It has been marked as inactive instead.");
                }
                else
                {
                    // If no related records, proceed with physical deletion
                    Debug.WriteLine("Performing physical delete - no related records found");
                    await _repository.DeleteAsync(customer);
                    await _unitOfWork.SaveChangesAsync();

                    var dto = _mapper.Map<CustomerDTO>(customer);
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Delete", dto));

                    await transaction.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in DeleteAsync: {ex.Message}");
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Add a new method for soft delete only
        public async Task SoftDeleteAsync(int id)
        {
            Debug.WriteLine($"Starting SoftDeleteAsync for customer {id}");

            var customer = await _repository.GetByIdAsync(id);
            if (customer == null)
            {
                Debug.WriteLine($"Customer with ID {id} not found");
                throw new InvalidOperationException($"Customer with ID {id} not found");
            }

            customer.IsActive = false;

            await _repository.UpdateAsync(customer);
            await _unitOfWork.SaveChangesAsync();

            var dto = _mapper.Map<CustomerDTO>(customer);
            _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>("Update", dto));

            Debug.WriteLine($"Customer {id} soft deleted successfully");
        }
    }
}