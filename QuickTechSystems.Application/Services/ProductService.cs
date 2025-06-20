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
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .Where(p => p.CategoryId == categoryId)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetLowStockProductsAsync()
        {
            return await GetLowStockProductsAsync(null);
        }

        public async Task<IEnumerable<ProductDTO>> GetLowStockProductsAsync(int? customThreshold = null)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
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
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .Where(p => p.Barcode == barcode);

                if (excludeProductId > 0)
                {
                    query = query.Where(p => p.ProductId != excludeProductId);
                }

                var product = await query.FirstOrDefaultAsync();
                return _mapper.Map<ProductDTO>(product);
            });
        }

        public async Task<bool> UpdateStockAsync(int productId, decimal quantity)
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

                    decimal oldStock = product.CurrentStock;

                    product.CurrentStock = decimal.Add(product.CurrentStock, quantity);
                    product.UpdatedAt = DateTime.Now;

                    await _repository.UpdateAsync(product);
                    await _unitOfWork.SaveChangesAsync();

                    Debug.WriteLine($"Stock updated for product {productId}: {oldStock} → {product.CurrentStock}");

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
                    var existingProduct = await _repository.GetByIdAsync(dto.ProductId);
                    if (existingProduct == null)
                    {
                        throw new InvalidOperationException($"Product with ID {dto.ProductId} not found");
                    }

                    _mapper.Map(dto, existingProduct);

                    await _repository.UpdateAsync(existingProduct);
                    await _unitOfWork.SaveChangesAsync();

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
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
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
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
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

        public async Task<List<ProductDTO>> CreateBatchAsync(List<ProductDTO> products, IProgress<string>? progress = null)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                Debug.WriteLine($"Starting batch create for {products.Count} products");
                var savedProducts = new List<ProductDTO>();

                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    for (int i = 0; i < products.Count; i++)
                    {
                        var product = products[i];
                        progress?.Report($"Saving product {i + 1} of {products.Count}: {product.Name}");

                        var entity = _mapper.Map<Product>(product);

                        if (entity.CreatedAt == default)
                            entity.CreatedAt = DateTime.Now;

                        var result = await _repository.AddAsync(entity);

                        var resultDto = _mapper.Map<ProductDTO>(result);
                        savedProducts.Add(resultDto);
                    }

                    await _unitOfWork.SaveChangesAsync();

                    await transaction.CommitAsync();

                    foreach (var savedProduct in savedProducts)
                    {
                        _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Create", savedProduct));
                    }

                    Debug.WriteLine($"Successfully saved {savedProducts.Count} products in batch");
                    return savedProducts;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in batch save: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }

                    try
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine("Transaction rolled back successfully");
                    }
                    catch (Exception rollbackEx)
                    {
                        Debug.WriteLine($"Error rolling back transaction: {rollbackEx.Message}");
                    }

                    throw;
                }
            });
        }

        public override async Task<ProductDTO> GetByIdAsync(int id)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var product = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .FirstOrDefaultAsync(p => p.ProductId == id);
                return _mapper.Map<ProductDTO>(product);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetActiveAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .Where(p => p.IsActive)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> SearchProductsAsync(string searchTerm)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var query = _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .Where(p => p.IsActive);

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    searchTerm = searchTerm.ToLower();
                    query = query.Where(p =>
                        p.Name.ToLower().Contains(searchTerm) ||
                        p.Barcode.ToLower().Contains(searchTerm) ||
                        (p.Description != null && p.Description.ToLower().Contains(searchTerm)) ||
                        (p.Category != null && p.Category.Name.ToLower().Contains(searchTerm)) ||
                        (p.Supplier != null && p.Supplier.Name.ToLower().Contains(searchTerm)) ||
                        (p.PlantsHardscape != null && p.PlantsHardscape.Name.ToLower().Contains(searchTerm)) ||
                        (p.LocalImported != null && p.LocalImported.Name.ToLower().Contains(searchTerm)) ||
                        (p.IndoorOutdoor != null && p.IndoorOutdoor.Name.ToLower().Contains(searchTerm)) ||
                        (p.PlantFamily != null && p.PlantFamily.Name.ToLower().Contains(searchTerm)) ||
                        (p.Detail != null && p.Detail.Name.ToLower().Contains(searchTerm))
                    );
                }

                var products = await query.OrderBy(p => p.Name).ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetProductsByPlantsHardscapeAsync(int plantsHardscapeId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .Where(p => p.PlantsHardscapeId == plantsHardscapeId && p.IsActive)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetProductsByLocalImportedAsync(int localImportedId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .Where(p => p.LocalImportedId == localImportedId && p.IsActive)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetProductsByIndoorOutdoorAsync(int indoorOutdoorId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .Where(p => p.IndoorOutdoorId == indoorOutdoorId && p.IsActive)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetProductsByPlantFamilyAsync(int plantFamilyId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .Where(p => p.PlantFamilyId == plantFamilyId && p.IsActive)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetProductsByDetailAsync(int detailId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Include(p => p.PlantsHardscape)
                    .Include(p => p.LocalImported)
                    .Include(p => p.IndoorOutdoor)
                    .Include(p => p.PlantFamily)
                    .Include(p => p.Detail)
                    .Where(p => p.DetailId == detailId && p.IsActive)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }
    }
}