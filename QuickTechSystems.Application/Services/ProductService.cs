using System.Diagnostics;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces;
using Microsoft.Data.SqlClient;

namespace QuickTechSystems.Application.Services
{
    public class ProductService : BaseService<Product, ProductDTO>, IProductService
    {
        private readonly Dictionary<int, SemaphoreSlim> _productLocks;
        private readonly SemaphoreSlim _lockManagerSemaphore;

        public ProductService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _productLocks = new Dictionary<int, SemaphoreSlim>();
            _lockManagerSemaphore = new SemaphoreSlim(1, 1);
        }

        private async Task<SemaphoreSlim> GetProductLock(int productId)
        {
            await _lockManagerSemaphore.WaitAsync();
            try
            {
                if (!_productLocks.ContainsKey(productId))
                {
                    _productLocks[productId] = new SemaphoreSlim(1, 1);
                }
                return _productLocks[productId];
            }
            finally
            {
                _lockManagerSemaphore.Release();
            }
        }

        public async Task<ProductDTO?> GetByBarcodeAsync(string barcode)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var product = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Barcode == barcode);
                return _mapper.Map<ProductDTO>(product);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetByCategoryAsync(int categoryId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Where(p => p.CategoryId == categoryId)
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .AsNoTracking()
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetBySupplierAsync(int supplierId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Where(p => p.SupplierId == supplierId)
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .AsNoTracking()
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetActiveAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Where(p => p.IsActive)
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .AsNoTracking()
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> GetLowStockAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Where(p => p.CurrentStock <= p.MinimumStock && p.IsActive)
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .AsNoTracking()
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<IEnumerable<ProductDTO>> SearchByNameAsync(string name)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Where(p => p.Name.Contains(name) && p.IsActive)
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .AsNoTracking()
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public async Task<bool> IsBarcodeUniqueAsync(string barcode, int? excludeId = null)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var query = _repository.Query().Where(p => p.Barcode == barcode);
                if (excludeId.HasValue)
                {
                    query = query.Where(p => p.ProductId != excludeId.Value);
                }
                return !await query.AsNoTracking().AnyAsync();
            });
        }

        public async Task<bool> TransferFromStorehouseAsync(int productId, decimal quantity)
        {
            var productLock = await GetProductLock(productId);
            await productLock.WaitAsync();
            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var productData = await _repository.Query()
                            .Where(p => p.ProductId == productId)
                            .Select(p => new { p.ProductId, p.Storehouse, p.CurrentStock })
                            .AsNoTracking()
                            .FirstOrDefaultAsync();

                        if (productData == null)
                        {
                            throw new InvalidOperationException($"Product with ID {productId} not found");
                        }

                        if (productData.Storehouse < quantity)
                        {
                            throw new InvalidOperationException("Insufficient quantity in storehouse");
                        }

                        if (quantity <= 0)
                        {
                            throw new InvalidOperationException("Transfer quantity must be greater than zero");
                        }

                        var updateParameters = new[]
                        {
                            new SqlParameter("@productId", productId),
                            new SqlParameter("@quantity", quantity),
                            new SqlParameter("@updatedAt", DateTime.Now)
                        };

                        string updateSql = @"
                            UPDATE Products 
                            SET Storehouse = Storehouse - @quantity,
                                CurrentStock = CurrentStock + @quantity,
                                UpdatedAt = @updatedAt
                            WHERE ProductId = @productId";

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(updateSql, updateParameters);

                        var newCurrentStock = productData.CurrentStock + quantity;

                        var inventoryHistory = new InventoryHistory
                        {
                            ProductId = productId,
                            QuantityChange = quantity,
                            NewQuantity = newCurrentStock,
                            Type = "Transfer",
                            Notes = $"Transferred {quantity} items from storehouse to stock",
                            Timestamp = DateTime.Now
                        };

                        await _unitOfWork.Context.Set<InventoryHistory>().AddAsync(inventoryHistory);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        var updatedProduct = await GetByIdAsync(productId);
                        if (updatedProduct != null)
                        {
                            _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", updatedProduct));
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"Error transferring from storehouse: {ex}");
                        throw;
                    }
                });
            }
            finally
            {
                productLock.Release();
            }
        }

        public async Task<bool> TransferBoxesFromStorehouseAsync(int productId, int boxQuantity)
        {
            var productLock = await GetProductLock(productId);
            await productLock.WaitAsync();
            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var productData = await _repository.Query()
                            .Where(p => p.ProductId == productId)
                            .Select(p => new {
                                p.ProductId,
                                p.Storehouse,
                                p.CurrentStock,
                                p.ItemsPerBox,
                                p.NumberOfBoxes
                            })
                            .AsNoTracking()
                            .FirstOrDefaultAsync();

                        if (productData == null)
                        {
                            throw new InvalidOperationException($"Product with ID {productId} not found");
                        }

                        if (productData.ItemsPerBox <= 0)
                        {
                            throw new InvalidOperationException("Items per box must be configured for box transfers");
                        }

                        var totalItemsToTransfer = boxQuantity * productData.ItemsPerBox;

                        if (productData.Storehouse < totalItemsToTransfer)
                        {
                            throw new InvalidOperationException("Insufficient quantity in storehouse for box transfer");
                        }

                        if (boxQuantity <= 0)
                        {
                            throw new InvalidOperationException("Box quantity must be greater than zero");
                        }

                        decimal availableBoxes = Math.Floor(productData.Storehouse / productData.ItemsPerBox);
                        if (boxQuantity > availableBoxes)
                        {
                            throw new InvalidOperationException($"Insufficient complete boxes in storehouse. Available: {availableBoxes} boxes");
                        }

                        var updateParameters = new[]
                        {
                            new SqlParameter("@productId", productId),
                            new SqlParameter("@itemsToTransfer", totalItemsToTransfer),
                            new SqlParameter("@boxQuantity", boxQuantity),
                            new SqlParameter("@updatedAt", DateTime.Now)
                        };

                        string updateSql = @"
                            UPDATE Products 
                            SET Storehouse = Storehouse - @itemsToTransfer,
                                CurrentStock = CurrentStock + @itemsToTransfer,
                                NumberOfBoxes = NumberOfBoxes - @boxQuantity,
                                UpdatedAt = @updatedAt
                            WHERE ProductId = @productId";

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(updateSql, updateParameters);

                        var newCurrentStock = productData.CurrentStock + totalItemsToTransfer;

                        var inventoryHistory = new InventoryHistory
                        {
                            ProductId = productId,
                            QuantityChange = totalItemsToTransfer,
                            NewQuantity = newCurrentStock,
                            Type = "BoxTransfer",
                            Notes = $"Transferred {boxQuantity} boxes ({totalItemsToTransfer} items) from storehouse to stock",
                            Timestamp = DateTime.Now
                        };

                        await _unitOfWork.Context.Set<InventoryHistory>().AddAsync(inventoryHistory);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        var updatedProduct = await GetByIdAsync(productId);
                        if (updatedProduct != null)
                        {
                            _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", updatedProduct));
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"Error transferring boxes from storehouse: {ex}");
                        throw;
                    }
                });
            }
            finally
            {
                productLock.Release();
            }
        }

        public async Task<ProductDTO> GenerateBarcodeAsync(ProductDTO product)
        {
            if (string.IsNullOrEmpty(product.Barcode))
            {
                product.Barcode = DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(1000, 9999);
            }
            return product;
        }

        public override async Task<IEnumerable<ProductDTO>> GetAllAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var products = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .AsNoTracking()
                    .ToListAsync();
                return _mapper.Map<IEnumerable<ProductDTO>>(products);
            });
        }

        public override async Task<ProductDTO?> GetByIdAsync(int id)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var product = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ProductId == id);
                return _mapper.Map<ProductDTO>(product);
            });
        }

        public override async Task<ProductDTO> CreateAsync(ProductDTO dto)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var isUnique = await IsBarcodeUniqueAsync(dto.Barcode);
                    if (!isUnique)
                    {
                        throw new InvalidOperationException($"A product with barcode '{dto.Barcode}' already exists.");
                    }

                    var entity = _mapper.Map<Product>(dto);
                    entity.CreatedAt = DateTime.Now;

                    var result = await _repository.AddAsync(entity);
                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var resultDto = _mapper.Map<ProductDTO>(result);
                    _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Create", resultDto));

                    return resultDto;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine($"Error creating product: {ex.Message}");
                    throw;
                }
            });
        }

        public override async Task UpdateAsync(ProductDTO dto)
        {
            var productLock = await GetProductLock(dto.ProductId);
            await productLock.WaitAsync();
            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var isUnique = await IsBarcodeUniqueAsync(dto.Barcode, dto.ProductId);
                        if (!isUnique)
                        {
                            throw new InvalidOperationException($"Another product with barcode '{dto.Barcode}' already exists.");
                        }

                        var existingProductData = await _repository.Query()
                            .Where(p => p.ProductId == dto.ProductId)
                            .Select(p => new { p.ProductId })
                            .AsNoTracking()
                            .FirstOrDefaultAsync();

                        if (existingProductData == null)
                        {
                            throw new InvalidOperationException($"Product with ID {dto.ProductId} not found");
                        }

                        _unitOfWork.DetachAllEntities();

                        var updateParameters = new List<SqlParameter>
                        {
                            new SqlParameter("@productId", dto.ProductId),
                            new SqlParameter("@barcode", dto.Barcode ?? string.Empty),
                            new SqlParameter("@name", dto.Name ?? string.Empty),
                            new SqlParameter("@description", (object)dto.Description ?? DBNull.Value),
                            new SqlParameter("@categoryId", dto.CategoryId),
                            new SqlParameter("@supplierId", (object)dto.SupplierId ?? DBNull.Value),
                            new SqlParameter("@purchasePrice", dto.PurchasePrice),
                            new SqlParameter("@salePrice", dto.SalePrice),
                            new SqlParameter("@currentStock", dto.CurrentStock),
                            new SqlParameter("@storehouse", dto.Storehouse),
                            new SqlParameter("@minimumStock", dto.MinimumStock),
                            new SqlParameter("@isActive", dto.IsActive),
                            new SqlParameter("@imagePath", (object)dto.ImagePath ?? DBNull.Value),
                            new SqlParameter("@boxBarcode", dto.BoxBarcode ?? string.Empty),
                            new SqlParameter("@numberOfBoxes", dto.NumberOfBoxes),
                            new SqlParameter("@itemsPerBox", dto.ItemsPerBox),
                            new SqlParameter("@boxPurchasePrice", dto.BoxPurchasePrice),
                            new SqlParameter("@boxSalePrice", dto.BoxSalePrice),
                            new SqlParameter("@minimumBoxStock", dto.MinimumBoxStock),
                            new SqlParameter("@wholesalePrice", dto.WholesalePrice),
                            new SqlParameter("@boxWholesalePrice", dto.BoxWholesalePrice),
                            new SqlParameter("@updatedAt", DateTime.Now)
                        };

                        string updateSql = @"
                            UPDATE Products 
                            SET Barcode = @barcode,
                                Name = @name,
                                Description = @description,
                                CategoryId = @categoryId,
                                SupplierId = @supplierId,
                                PurchasePrice = @purchasePrice,
                                SalePrice = @salePrice,
                                CurrentStock = @currentStock,
                                Storehouse = @storehouse,
                                MinimumStock = @minimumStock,
                                IsActive = @isActive,
                                ImagePath = @imagePath,
                                BoxBarcode = @boxBarcode,
                                NumberOfBoxes = @numberOfBoxes,
                                ItemsPerBox = @itemsPerBox,
                                BoxPurchasePrice = @boxPurchasePrice,
                                BoxSalePrice = @boxSalePrice,
                                MinimumBoxStock = @minimumBoxStock,
                                WholesalePrice = @wholesalePrice,
                                BoxWholesalePrice = @boxWholesalePrice,
                                UpdatedAt = @updatedAt
                            WHERE ProductId = @productId";

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(updateSql, updateParameters.ToArray());
                        await transaction.CommitAsync();

                        _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", dto));
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"Error updating product: {ex}");
                        throw;
                    }
                });
            }
            finally
            {
                productLock.Release();
            }
        }

        public override async Task DeleteAsync(int id)
        {
            var productLock = await GetProductLock(id);
            await productLock.WaitAsync();
            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var hasTransactions = await _unitOfWork.Context.Set<TransactionDetail>()
                            .Where(td => td.ProductId == id)
                            .AsNoTracking()
                            .AnyAsync();

                        if (hasTransactions)
                        {
                            throw new InvalidOperationException(
                                "Cannot delete product because it has associated transactions. " +
                                "Please mark it as inactive instead.");
                        }

                        var productData = await _repository.Query()
                            .Where(p => p.ProductId == id)
                            .Select(p => new { p.ProductId, p.Name, p.Barcode })
                            .AsNoTracking()
                            .FirstOrDefaultAsync();

                        if (productData == null)
                        {
                            throw new InvalidOperationException($"Product with ID {id} not found");
                        }

                        string deleteSql = "DELETE FROM Products WHERE ProductId = @productId";
                        var deleteParameters = new[] { new SqlParameter("@productId", id) };

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(deleteSql, deleteParameters);
                        await transaction.CommitAsync();

                        var productDto = new ProductDTO { ProductId = id, Name = productData.Name, Barcode = productData.Barcode };
                        _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Delete", productDto));
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"Error deleting product: {ex}");
                        throw;
                    }
                });
            }
            finally
            {
                productLock.Release();
            }
        }
    }
}