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
    public class ProductService : BaseService<Product, ProductDTO>, IProductService
    {
        public ProductService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
        }

        public async Task<IEnumerable<ProductDTO>> GetByCategoryAsync(int categoryId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Where(p => p.CategoryId == categoryId)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        // Parameterless method to satisfy the IProductService interface
        public async Task<IEnumerable<ProductDTO>> GetLowStockProductsAsync()
        {
            return await GetLowStockProductsAsync(null);
        }

        // Existing method with optional customThreshold parameter
        public async Task<IEnumerable<ProductDTO>> GetLowStockProductsAsync(int? customThreshold = null)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Where(p => p.CurrentStock <= (customThreshold ?? p.MinimumStock))
                    .ToListAsync();

                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<ProductDTO> FindProductByBarcodeAsync(string barcode, int excludeProductId = 0)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var query = _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Where(p => p.Barcode == barcode);

                if (excludeProductId > 0)
                {
                    query = query.Where(p => p.ProductId != excludeProductId);
                }

                var product = await query.FirstOrDefaultAsync();
                return _mapper.Map<ProductDTO>(product);
            });
        }
        public async Task<bool> UpdateStockAsync(int productId, int quantity)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    Debug.WriteLine($"Updating stock for product {productId} by {quantity}");

                    var product = await _repository.GetByIdAsync(productId);
                    if (product == null)
                    {
                        Debug.WriteLine($"Product {productId} not found for stock update");
                        return false;
                    }

                    // Update stock and timestamp - no validation checking for negative values
                    int oldStock = product.CurrentStock;
                    product.CurrentStock = product.CurrentStock + quantity;
                    product.UpdatedAt = DateTime.Now;

                    await _repository.UpdateAsync(product);
                    await _unitOfWork.SaveChangesAsync();

                    Debug.WriteLine($"Stock updated for product {productId}: {oldStock} → {product.CurrentStock}");

                    // Explicitly publish event about stock change
                    var productDto = _mapper.Map<ProductDTO>(product);
                    _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", productDto));

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating product stock: {ex.Message}");
                    throw;
                }
            });
        }

        public override async Task UpdateAsync(ProductDTO dto)
        {
            await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    // Get the existing entity from the context
                    var existingProduct = await _repository.GetByIdAsync(dto.ProductId);
                    if (existingProduct == null)
                    {
                        throw new InvalidOperationException($"Product with ID {dto.ProductId} not found");
                    }

                    // Update the existing entity properties
                    _mapper.Map(dto, existingProduct);

                    await _repository.UpdateAsync(existingProduct);
                    await _unitOfWork.SaveChangesAsync();

                    // Publish the update event
                    _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", dto));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating product: {ex}");
                    throw;
                }
            });
        }

        public override async Task<IEnumerable<ProductDTO>> GetAllAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<ProductDTO?> GetByBarcodeAsync(string barcode)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var product = await _repository.Query()
                    .Include(p => p.Category)
                    .FirstOrDefaultAsync(p => p.Barcode == barcode);
                return _mapper.Map<ProductDTO>(product);
            });
        }

        public override async Task<ProductDTO> CreateAsync(ProductDTO dto)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                Debug.WriteLine("Starting create in service");
                var entity = _mapper.Map<Product>(dto);
                var result = await _repository.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync();
                var resultDto = _mapper.Map<ProductDTO>(result);
                Debug.WriteLine("Publishing create event");
                _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Create", resultDto));
                Debug.WriteLine("Create event published");
                return resultDto;
            });
        }
    }
}