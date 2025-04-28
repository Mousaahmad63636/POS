// Path: QuickTechSystems.Application.Services/MainStockService.cs
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
    public class MainStockService : BaseService<MainStock, MainStockDTO>, IMainStockService
    {
        private readonly IInventoryTransferService _inventoryTransferService;
        private readonly IProductService _productService; // Add missing ProductService

        public MainStockService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService,
            IInventoryTransferService inventoryTransferService,
            IProductService productService) // Add parameter
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _inventoryTransferService = inventoryTransferService;
            _productService = productService; // Store reference
        }

        public async Task<IEnumerable<MainStockDTO>> GetByCategoryAsync(int categoryId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Where(p => p.CategoryId == categoryId)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<MainStockDTO>>(products);
            });
        }

        public async Task<IEnumerable<MainStockDTO>> GetLowStockProductsAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Where(p => p.CurrentStock <= p.MinimumStock)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<MainStockDTO>>(products);
            });
        }

        public async Task<MainStockDTO> FindProductByBarcodeAsync(string barcode, int excludeMainStockId = 0)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var query = _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .Where(p => p.Barcode == barcode);

                if (excludeMainStockId > 0)
                {
                    query = query.Where(p => p.MainStockId != excludeMainStockId);
                }

                var product = await query.FirstOrDefaultAsync();
                return _mapper.Map<MainStockDTO>(product);
            });
        }

        public async Task<bool> UpdateStockAsync(int mainStockId, decimal quantity)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    Debug.WriteLine($"Updating stock for MainStock {mainStockId} by {quantity}");

                    var product = await _repository.GetByIdAsync(mainStockId);
                    if (product == null)
                    {
                        Debug.WriteLine($"MainStock {mainStockId} not found for stock update");
                        return false;
                    }

                    // Store original stock for logging
                    decimal oldStock = product.CurrentStock;

                    // Calculate new stock - ensure we use exact decimal math with no rounding
                    product.CurrentStock = decimal.Add(product.CurrentStock, quantity);
                    product.UpdatedAt = DateTime.Now;

                    await _repository.UpdateAsync(product);
                    await _unitOfWork.SaveChangesAsync();

                    Debug.WriteLine($"Stock updated for MainStock {mainStockId}: {oldStock} → {product.CurrentStock}");

                    // Publish update event
                    var productDto = _mapper.Map<MainStockDTO>(product);
                    _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>("Update", productDto));

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating MainStock: {ex.Message}");
                    throw;
                }
            });
        }

        public override async Task<MainStockDTO> CreateAsync(MainStockDTO dto)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                Debug.WriteLine("Starting create in MainStockService");

                // Ensure the created date is set
                if (dto.CreatedAt == default)
                {
                    dto.CreatedAt = DateTime.Now;
                }

                var entity = _mapper.Map<MainStock>(dto);
                var result = await _repository.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync();
                var resultDto = _mapper.Map<MainStockDTO>(result);

                Debug.WriteLine("Publishing create event for MainStock");
                _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>("Create", resultDto));

                return resultDto;
            });
        }

        public async Task<MainStockDTO?> GetByBarcodeAsync(string barcode)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var product = await _repository.Query()
                    .Include(p => p.Category)
                    .FirstOrDefaultAsync(p => p.Barcode == barcode);
                return _mapper.Map<MainStockDTO>(product);
            });
        }

        // Path: QuickTechSystems.Application.Services/MainStockService.cs
        public async Task<List<MainStockDTO>> CreateBatchAsync(List<MainStockDTO> products, IProgress<string>? progress = null)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                Debug.WriteLine($"Starting batch create for {products.Count} MainStock items");
                var savedProducts = new List<MainStockDTO>();

                // Start a transaction for the entire batch operation
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    for (int i = 0; i < products.Count; i++)
                    {
                        var product = products[i];
                        progress?.Report($"Saving product {i + 1} of {products.Count}: {product.Name}");

                        var entity = _mapper.Map<MainStock>(product);

                        // Ensure the CreatedAt is set
                        if (entity.CreatedAt == default)
                            entity.CreatedAt = DateTime.Now;

                        // Explicitly check for existing entity to avoid tracking conflicts
                        MainStock result;
                        if (entity.MainStockId > 0)
                        {
                            // For existing entities, first check if it exists in the database
                            var existingEntity = await _repository.GetByIdAsync(entity.MainStockId);
                            if (existingEntity != null)
                            {
                                // Detach the existing entity to avoid tracking conflicts
                                _unitOfWork.DetachEntity(existingEntity);

                                // Update the entity
                                entity.UpdatedAt = DateTime.Now;
                                await _repository.UpdateAsync(entity);
                                result = entity; // Use the entity object directly since UpdateAsync returns void
                            }
                            else
                            {
                                // If not found (unusual case), treat as new entity
                                entity.MainStockId = 0; // Reset ID to let database assign a new one
                                result = await _repository.AddAsync(entity);
                            }
                        }
                        else
                        {
                            // New entity, just add it
                            result = await _repository.AddAsync(entity);
                        }

                        // Map back to DTO without saving changes yet
                        var resultDto = _mapper.Map<MainStockDTO>(result);
                        savedProducts.Add(resultDto);
                    }

                    // Save all changes in a single database operation
                    await _unitOfWork.SaveChangesAsync();

                    // Commit the transaction only after successful save
                    await transaction.CommitAsync();

                    // Publish events for all saved products
                    foreach (var savedProduct in savedProducts)
                    {
                        _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>(
                            savedProduct.MainStockId > 0 ? "Update" : "Create",
                            savedProduct));
                    }

                    Debug.WriteLine($"Successfully saved {savedProducts.Count} MainStock items in batch");
                    return savedProducts;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in batch save: {ex.Message}");
                    // Log inner exception details for debugging
                    if (ex.InnerException != null)
                    {
                        Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }

                    try
                    {
                        // Attempt to rollback on error
                        await transaction.RollbackAsync();
                        Debug.WriteLine("Transaction rolled back successfully");
                    }
                    catch (Exception rollbackEx)
                    {
                        Debug.WriteLine($"Error rolling back transaction: {rollbackEx.Message}");
                        // Continue with throw, we still want to report the original error
                    }

                    throw; // Rethrow to let caller handle it
                }
            });
        }
        public async Task<bool> TransferToStoreAsync(int mainStockId, int productId, decimal quantity, string transferredBy, string notes)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    // Step 1: Get the MainStock item
                    var mainStockItem = await _repository.GetByIdAsync(mainStockId);
                    if (mainStockItem == null)
                    {
                        Debug.WriteLine($"MainStock item {mainStockId} not found for transfer");
                        return false;
                    }

                    // Validate we have enough stock
                    if (mainStockItem.CurrentStock < quantity)
                    {
                        Debug.WriteLine($"Insufficient stock in MainStock item {mainStockId}. Available: {mainStockItem.CurrentStock}, Requested: {quantity}");
                        return false;
                    }

                    // Step 2: Reduce the MainStock quantity
                    decimal oldMainStockQty = mainStockItem.CurrentStock;
                    mainStockItem.CurrentStock -= quantity;
                    mainStockItem.UpdatedAt = DateTime.Now;

                    await _repository.UpdateAsync(mainStockItem);

                    // Step 3: Use InventoryTransferService to create transfer record
                    var transferDto = new InventoryTransferDTO
                    {
                        MainStockId = mainStockId,
                        ProductId = productId,
                        Quantity = quantity,
                        TransferredBy = transferredBy,
                        Notes = notes,
                        TransferDate = DateTime.Now,
                        ReferenceNumber = $"TRF-{DateTime.Now:yyyyMMddHHmmss}-{mainStockId}-{productId}"
                    };

                    // Note: We're not using the full CreateAsync from InventoryTransferService
                    // since it would duplicate the stock adjustments we're already doing here
                    var transfer = _mapper.Map<InventoryTransfer>(transferDto);
                    await _unitOfWork.InventoryTransfers.AddAsync(transfer);

                    // Step 4: Transfer the inventory to the store product
                    bool transferSuccess = await _productService.ReceiveInventoryAsync(
                        productId,
                        quantity,
                        "MainStock",
                        $"Transfer from MainStock ID: {mainStockId}"
                    );

                    if (!transferSuccess)
                    {
                        throw new Exception($"Failed to transfer inventory to product ID {productId}");
                    }

                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Step 5: Publish events - CRITICAL FOR REAL-TIME UPDATES
                    var mainStockDto = _mapper.Map<MainStockDTO>(mainStockItem);
                    _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>("Update", mainStockDto));

                    // Manually publish product stock update event to ensure real-time updates in Product view
                    _eventAggregator.Publish(new ProductStockUpdatedEvent(productId, await GetCurrentProductStock(productId)));

                    Debug.WriteLine($"Successfully transferred {quantity} units from MainStock {mainStockId} to Product {productId}");
                    return true;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine($"Error in MainStock transfer: {ex.Message}");
                    throw;
                }
            });
        }

        // Helper method to get the current product stock
        private async Task<decimal> GetCurrentProductStock(int productId)
        {
            var product = await _unitOfWork.Products.GetByIdAsync(productId);
            return product?.CurrentStock ?? 0;
        }

        // Path: QuickTechSystems.Application.Services/MainStockService.cs
        public override async Task<MainStockDTO> UpdateAsync(MainStockDTO dto)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                Debug.WriteLine($"Updating MainStock item {dto.MainStockId}: {dto.Name}");

                try
                {
                    // Set updated timestamp
                    if (dto.UpdatedAt == null)
                    {
                        dto.UpdatedAt = DateTime.Now;
                    }

                    // Find existing entity first
                    var existingEntity = await _repository.GetByIdAsync(dto.MainStockId);
                    if (existingEntity != null)
                    {
                        // Detach the existing entity to avoid tracking conflicts
                        _unitOfWork.DetachEntity(existingEntity);
                    }

                    // Map DTO to entity
                    var entity = _mapper.Map<MainStock>(dto);

                    // Update the entity and save changes in one operation
                    await _repository.UpdateAsync(entity);
                    await _unitOfWork.SaveChangesAsync();

                    // Map back to DTO for result
                    var resultDto = _mapper.Map<MainStockDTO>(entity);

                    // Publish the update event
                    _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>("Update", resultDto));

                    return resultDto;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating MainStock: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }
                    throw;
                }
            });
        }
        public async Task<IEnumerable<MainStockDTO>> SearchAsync(string searchTerm)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var query = _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    searchTerm = searchTerm.ToLower();
                    query = query.Where(p =>
                        p.Name.ToLower().Contains(searchTerm) ||
                        p.Barcode.ToLower().Contains(searchTerm) ||
                        (p.Category != null && p.Category.Name.ToLower().Contains(searchTerm)) ||
                        (p.Supplier != null && p.Supplier.Name.ToLower().Contains(searchTerm)) ||
                        (p.Description != null && p.Description.ToLower().Contains(searchTerm))
                    );
                }

                var products = await query.ToListAsync();
                return _mapper.Map<IEnumerable<MainStockDTO>>(products);
            });
        }
    }
}