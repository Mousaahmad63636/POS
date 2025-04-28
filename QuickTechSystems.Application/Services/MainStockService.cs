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

        public MainStockService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService,
            IInventoryTransferService inventoryTransferService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _inventoryTransferService = inventoryTransferService;
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

                        var result = await _repository.AddAsync(entity);

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
                        _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>("Create", savedProduct));
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
        public async Task<bool> TransferToStoreAsync(int mainStockId, int productId, decimal quantity, string transferredBy, string? notes = null)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    Debug.WriteLine($"Starting transfer from MainStock {mainStockId} to Product {productId}, quantity: {quantity}");

                    // Get the MainStock item
                    var mainStock = await _repository.GetByIdAsync(mainStockId);
                    if (mainStock == null)
                    {
                        Debug.WriteLine($"MainStock with id {mainStockId} not found");
                        return false;
                    }

                    // Check if we have enough stock to transfer
                    if (mainStock.CurrentStock < quantity)
                    {
                        Debug.WriteLine($"Insufficient stock in MainStock {mainStock.Name}: {mainStock.CurrentStock} < {quantity}");
                        return false;
                    }

                    // Get the Product
                    var product = await _unitOfWork.Products.GetByIdAsync(productId);
                    if (product == null)
                    {
                        Debug.WriteLine($"Product with id {productId} not found");
                        return false;
                    }

                    // Ensure no circular reference by temporarily clearing MainStockId if needed
                    if (product.MainStockId.HasValue && product.MainStockId.Value == mainStockId)
                    {
                        Debug.WriteLine("Clearing circular reference to MainStock from Product");
                        product.MainStockId = null;
                        await _unitOfWork.Products.UpdateAsync(product);
                        await _unitOfWork.SaveChangesAsync();
                    }

                    // Generate a reference number for the transfer
                    string referenceNumber = $"TRF-{DateTime.Now:yyyyMMddHHmmss}";

                    // Create transfer record
                    var transfer = new InventoryTransfer
                    {
                        MainStockId = mainStockId,
                        ProductId = productId,
                        Quantity = quantity,
                        TransferDate = DateTime.Now,
                        Notes = notes,
                        ReferenceNumber = referenceNumber,
                        TransferredBy = transferredBy
                    };

                    // Add the transfer record
                    await _unitOfWork.InventoryTransfers.AddAsync(transfer);

                    // Update MainStock quantity (deduct)
                    mainStock.CurrentStock -= quantity;
                    mainStock.UpdatedAt = DateTime.Now;
                    await _repository.UpdateAsync(mainStock);

                    // Update Product quantity (add)
                    product.CurrentStock += (int)quantity;
                    product.UpdatedAt = DateTime.Now;
                    await _unitOfWork.Products.UpdateAsync(product);

                    // IMPORTANT FIX: Only create inventory history record for the Product (not for MainStock)
                    // because InventoryHistories can only reference ProductId values

                    // Add inventory history for the product only
                    var productHistory = new InventoryHistory
                    {
                        ProductId = productId, // This is a valid ProductId
                        QuantityChange = quantity,
                        NewQuantity = product.CurrentStock,
                        Type = "Transfer-In",
                        Notes = $"Transfer from main stock: {referenceNumber} (MainStock ID: {mainStockId}, {mainStock.Name})",
                        Timestamp = DateTime.Now
                    };

                    await _unitOfWork.InventoryHistories.AddAsync(productHistory);

                    // Save all changes
                    Debug.WriteLine("Saving all transfer changes");
                    await _unitOfWork.SaveChangesAsync();
                    Debug.WriteLine("All changes saved successfully");
                    await transaction.CommitAsync();
                    Debug.WriteLine("Transaction committed");

                    // Explicitly force a refresh of both ViewModels
                    var mainStockDto = _mapper.Map<MainStockDTO>(mainStock);
                    _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>("Update", mainStockDto));

                    var productDto = _mapper.Map<ProductDTO>(product);
                    _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", productDto));

                    Debug.WriteLine($"Transfer completed: {quantity} units from MainStock {mainStock.Name} to Product {product.Name}");

                    // Force a reload of the product view data
                    _eventAggregator.Publish(new ProductStockUpdatedEvent(productId, product.CurrentStock));

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Transfer error: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }

                    await transaction.RollbackAsync();
                    Debug.WriteLine("Transaction rolled back");
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