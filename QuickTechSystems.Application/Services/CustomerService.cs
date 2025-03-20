﻿using System.Diagnostics;
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
        public CustomerService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
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
    }
}