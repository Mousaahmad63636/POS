using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class MonthlySubscriptionSettingsService : IMonthlySubscriptionSettingsService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IEventAggregator _eventAggregator;

        public MonthlySubscriptionSettingsService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _eventAggregator = eventAggregator;
        }

        public async Task<MonthlySubscriptionSettingsDTO> GetSettingsAsync()
        {
            var settings = await _unitOfWork.Context.Set<MonthlySubscriptionSettings>().FirstOrDefaultAsync();

            if (settings == null)
            {
                settings = new MonthlySubscriptionSettings
                {
                    DefaultUnitPrice = 0,
                    LastModified = DateTime.Now,
                    ModifiedBy = "System",
                    Currency = "USD"
                };
                await _unitOfWork.Context.Set<MonthlySubscriptionSettings>().AddAsync(settings);
                await _unitOfWork.SaveChangesAsync();
            }

            return _mapper.Map<MonthlySubscriptionSettingsDTO>(settings);
        }

        public async Task<MonthlySubscriptionSettingsDTO> UpdateSettingsAsync(MonthlySubscriptionSettingsDTO settings)
        {
            var entity = await _unitOfWork.Context.Set<MonthlySubscriptionSettings>().FirstOrDefaultAsync();
            if (entity == null)
            {
                entity = _mapper.Map<MonthlySubscriptionSettings>(settings);
                await _unitOfWork.Context.Set<MonthlySubscriptionSettings>().AddAsync(entity);
            }
            else
            {
                _mapper.Map(settings, entity);
            }

            entity.LastModified = DateTime.Now;
            await _unitOfWork.SaveChangesAsync();

            return _mapper.Map<MonthlySubscriptionSettingsDTO>(entity);
        }

        public async Task<decimal> GetDefaultUnitPriceAsync()
        {
            var settings = await GetSettingsAsync();
            return settings.DefaultUnitPrice;
        }

        public async Task UpdateAllCustomerPricesAsync(decimal newPrice, string modifiedBy)
        {
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                // Update settings
                var settings = await _unitOfWork.Context.Set<MonthlySubscriptionSettings>().FirstOrDefaultAsync();
                if (settings == null)
                {
                    settings = new MonthlySubscriptionSettings();
                    await _unitOfWork.Context.Set<MonthlySubscriptionSettings>().AddAsync(settings);
                }

                settings.DefaultUnitPrice = newPrice;
                settings.LastModified = DateTime.Now;
                settings.ModifiedBy = modifiedBy;

                // Update all customers
                var customers = await _unitOfWork.Context.Set<CustomerSubscription>().ToListAsync();
                foreach (var customer in customers)
                {
                    customer.PricePerUnit = newPrice;
                    customer.UpdatedAt = DateTime.Now;
                }

                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                // Publish event to notify of the price change
                _eventAggregator.Publish(new PriceUpdatedEvent(newPrice));
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task UpdateDefaultUnitPriceAsync(decimal newPrice, string modifiedBy)
        {
            var settings = await _unitOfWork.Context.Set<MonthlySubscriptionSettings>().FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new MonthlySubscriptionSettings();
                await _unitOfWork.Context.Set<MonthlySubscriptionSettings>().AddAsync(settings);
            }

            settings.DefaultUnitPrice = newPrice;
            settings.LastModified = DateTime.Now;
            settings.ModifiedBy = modifiedBy;

            await _unitOfWork.SaveChangesAsync();
        }
    }
}

// In a separate file in the Events namespace
namespace QuickTechSystems.Application.Events
{
    public class PriceUpdatedEvent
    {
        public decimal NewPrice { get; }

        public PriceUpdatedEvent(decimal newPrice)
        {
            NewPrice = newPrice;
        }
    }
}