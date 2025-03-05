using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class CustomerSubscriptionService : BaseService<CustomerSubscription, CustomerSubscriptionDTO>, ICustomerSubscriptionService
    {

        public CustomerSubscriptionService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator)
            : base(unitOfWork, mapper, unitOfWork.CustomerSubscriptions, eventAggregator)
        {
        }

        public new async Task<CustomerSubscriptionDTO> UpdateAsync(CustomerSubscriptionDTO dto)
        {
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var existingSubscription = await _repository.GetByIdAsync(dto.Id);
                if (existingSubscription == null)
                {
                    throw new InvalidOperationException($"Subscription with ID {dto.Id} not found");
                }

                _mapper.Map(dto, existingSubscription);
                await _repository.UpdateAsync(existingSubscription);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                // Map the updated entity back to DTO
                var updatedDto = _mapper.Map<CustomerSubscriptionDTO>(existingSubscription);

                // Publish update event
                _eventAggregator.Publish(new EntityChangedEvent<CustomerSubscriptionDTO>("Update", updatedDto));

                return updatedDto;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        public async Task<IEnumerable<CustomerSubscriptionDTO>> GetAllAsync()
        {
            try
            {
                var subscriptions = await _repository.Query()
                    .Include(c => c.SubscriptionType)
                    .OrderBy(c => c.Name)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<CustomerSubscriptionDTO>>(subscriptions);
            }
            catch (Exception)
            {
                throw;
            }
        }
        public async Task<IEnumerable<CustomerSubscriptionDTO>> GetByNameAsync(string name)
        {
            var customers = await _repository.Query()
                .Include(c => c.Payments)
                .Where(c => c.Name.Contains(name))
                .ToListAsync();
            return _mapper.Map<IEnumerable<CustomerSubscriptionDTO>>(customers);
        }

        public async Task<IEnumerable<CustomerSubscriptionDTO>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            var customers = await _repository.Query()
                .Include(c => c.Payments)
                .Where(c => c.LastBillDate >= startDate && c.LastBillDate <= endDate)
                .ToListAsync();
            return _mapper.Map<IEnumerable<CustomerSubscriptionDTO>>(customers);
        }

        public async Task<bool> UpdateCounterAsync(int customerId, decimal newCounter)
        {
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var customer = await _repository.GetByIdAsync(customerId);
                if (customer == null) return false;

                decimal unitsUsed = newCounter - customer.NewCounter;
                decimal billAmount = unitsUsed * customer.PricePerUnit;

                // Create counter history record
                var history = new CounterHistory
                {
                    CustomerSubscriptionId = customerId,
                    OldCounter = customer.NewCounter,
                    NewCounter = newCounter,
                    BillAmount = billAmount,
                    RecordDate = DateTime.Now
                };

                // Update customer
                customer.OldCounter = customer.NewCounter;
                customer.NewCounter = newCounter;
                customer.BillAmount = billAmount;
                customer.LastBillDate = DateTime.Now;
                customer.UpdatedAt = DateTime.Now;

                await _unitOfWork.Context.Set<CounterHistory>().AddAsync(history);
                await _repository.UpdateAsync(customer);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IEnumerable<CounterHistoryDTO>> GetCustomerHistoryAsync(int customerId)
        {
            var history = await _unitOfWork.Context.Set<CounterHistory>()
                .AsNoTracking()  // Add this
                .Include(h => h.CustomerSubscription)
                .Where(h => h.CustomerSubscriptionId == customerId)
                .ToListAsync();
            return _mapper.Map<IEnumerable<CounterHistoryDTO>>(history);
        }

        public async Task<IEnumerable<SubscriptionPaymentDTO>> GetCustomerPaymentsAsync(int customerId)
        {
            var payments = await _unitOfWork.Context.Set<SubscriptionPayment>()
                .Include(p => p.CustomerSubscription)
                .Where(p => p.CustomerSubscriptionId == customerId)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
            return _mapper.Map<IEnumerable<SubscriptionPaymentDTO>>(payments);
        }
        public async Task<bool> SaveCounterReadingAsync(int customerId, CounterHistoryDTO reading)
        {
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var customer = await _repository.GetByIdAsync(customerId);
                if (customer == null) return false;

                // Update customer's data
                customer.OldCounter = reading.OldCounter;
                customer.NewCounter = reading.NewCounter;
                customer.LastBillDate = reading.RecordDate;
                customer.BillAmount = reading.BillAmount;

                var counterHistory = new CounterHistory
                {
                    CustomerSubscriptionId = customerId,
                    OldCounter = reading.OldCounter,
                    NewCounter = reading.NewCounter,
                    BillAmount = reading.BillAmount,
                    RecordDate = reading.RecordDate,
                    PricePerUnit = reading.PricePerUnit
                };

                await _unitOfWork.Context.Set<CounterHistory>().AddAsync(counterHistory);
                await _repository.UpdateAsync(customer);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        public async Task<bool> ProcessPaymentAsync(SubscriptionPaymentDTO payment)
        {
            try
            {
                var paymentEntity = _mapper.Map<SubscriptionPayment>(payment);
                await _unitOfWork.Context.Set<SubscriptionPayment>().AddAsync(paymentEntity);
                await _unitOfWork.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}