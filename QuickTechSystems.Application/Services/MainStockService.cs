// Path: QuickTechSystems.Application.Services/MainStockService.cs
using System.Diagnostics;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        private readonly IProductService _productService;

        public MainStockService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService,
            IInventoryTransferService inventoryTransferService,
            IProductService productService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _inventoryTransferService = inventoryTransferService;
            _productService = productService;
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

        public async Task<MainStockDTO?> GetByBoxBarcodeAsync(string boxBarcode)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var product = await _repository.Query()
                    .Include(p => p.Category)
                    .Include(p => p.Supplier)
                    .FirstOrDefaultAsync(p => p.BoxBarcode == boxBarcode);
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

                // Ensure box barcode is set if barcode exists
                if (string.IsNullOrWhiteSpace(dto.BoxBarcode) && !string.IsNullOrWhiteSpace(dto.Barcode))
                {
                    dto.BoxBarcode = $"BX{dto.Barcode}";
                }

                var entity = _mapper.Map<MainStock>(dto);
                var result = await _repository.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync();

                // Verify that the ID was properly assigned
                if (result.MainStockId <= 0)
                {
                    Debug.WriteLine($"WARNING: MainStockId was not properly assigned for {dto.Name}");
                    throw new InvalidOperationException($"Database did not assign a valid ID to MainStock item: {dto.Name}");
                }

                var resultDto = _mapper.Map<MainStockDTO>(result);

                Debug.WriteLine($"Successfully created MainStock item with ID: {resultDto.MainStockId}");
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
                    .Include(p => p.Supplier)
                    .FirstOrDefaultAsync(p => p.Barcode == barcode);
                return _mapper.Map<MainStockDTO>(product);
            });
        }

        // UPDATED METHOD: Improved CreateBatchAsync with better error handling and individual item processing
        public async Task<List<MainStockDTO>> CreateBatchAsync(List<MainStockDTO> products, IProgress<string>? progress = null)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                Debug.WriteLine($"Starting batch create for {products.Count} MainStock items");
                var savedProducts = new List<MainStockDTO>();

                // Process each item individually for better error isolation
                for (int i = 0; i < products.Count; i++)
                {
                    var product = products[i];
                    try
                    {
                        progress?.Report($"Processing item {i + 1} of {products.Count}: {product.Name}");

                        // Check if this is an update or create
                        var existingProduct = !string.IsNullOrEmpty(product.Barcode)
                            ? await _repository.Query().FirstOrDefaultAsync(p => p.Barcode == product.Barcode)
                            : null;

                        if (existingProduct != null)
                        {
                            // This is an update - use the existing ID and update record
                            Debug.WriteLine($"Updating existing MainStock with barcode {product.Barcode}: {existingProduct.MainStockId}");

                            // Set the MainStockId from the existing record
                            product.MainStockId = existingProduct.MainStockId;

                            // Create a new entity to avoid tracking issues
                            var entity = _mapper.Map<MainStock>(product);
                            entity.CreatedAt = existingProduct.CreatedAt; // Preserve original creation date
                            entity.UpdatedAt = DateTime.Now;

                            // Detach the existing entity from tracking
                            _unitOfWork.DetachEntity(existingProduct);

                            // Update using repository
                            await _repository.UpdateAsync(entity);
                            await _unitOfWork.SaveChangesAsync();

                            // Refresh entity from database to get any computed properties
                            await _unitOfWork.Context.Entry(entity).ReloadAsync();

                            // Map back to DTO
                            var updatedDto = _mapper.Map<MainStockDTO>(entity);
                            savedProducts.Add(updatedDto);

                            // Publish event
                            _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>("Update", updatedDto));

                            Debug.WriteLine($"Successfully updated MainStock: {entity.Name} (ID: {entity.MainStockId})");
                        }
                        else
                        {
                            // This is a new item - create it
                            Debug.WriteLine($"Creating new MainStock: {product.Name}");

                            // Ensure CreatedAt is set
                            if (product.CreatedAt == default)
                                product.CreatedAt = DateTime.Now;

                            // Ensure box barcode is set if barcode exists
                            if (string.IsNullOrWhiteSpace(product.BoxBarcode) && !string.IsNullOrWhiteSpace(product.Barcode))
                                product.BoxBarcode = $"BX{product.Barcode}";

                            // Create a new entity
                            var entity = _mapper.Map<MainStock>(product);

                            // Add using repository
                            var addedEntity = await _repository.AddAsync(entity);
                            await _unitOfWork.SaveChangesAsync();

                            // Validate ID assignment
                            if (addedEntity.MainStockId <= 0)
                            {
                                Debug.WriteLine($"Warning: MainStockId was not assigned for {product.Name}");
                                throw new InvalidOperationException($"Database did not assign a valid ID to new MainStock: {product.Name}");
                            }

                            Debug.WriteLine($"Added new MainStock item {addedEntity.Name} with ID: {addedEntity.MainStockId}");

                            // Map back to DTO
                            var newDto = _mapper.Map<MainStockDTO>(addedEntity);
                            savedProducts.Add(newDto);

                            // Publish event
                            _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>("Create", newDto));
                        }

                        // Sync linked products if this is an update and MainStockId is valid
                        if (product.MainStockId > 0)
                        {
                            await SyncLinkedProductsForMainStock(product.MainStockId, product);
                        }

                        // Clear tracking every few items to avoid memory issues
                        if (i % 5 == 0)
                        {
                            _unitOfWork.Context.ChangeTracker.Clear();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing item {i + 1} ({product.Name}): {ex.Message}");
                        // Continue processing other items despite this error
                    }
                }

                Debug.WriteLine($"Batch processing complete. Successfully processed {savedProducts.Count} of {products.Count} items.");

                // Return the list of successfully saved products
                return savedProducts;
            });
        }

        // Private helper methods for direct database operations
        private async Task UpdateMainStockDirectly(MainStock item)
        {
            string sql = @"
        UPDATE MainStocks
        SET Name = @name,
            Barcode = @barcode,
            BoxBarcode = @boxBarcode,
            CategoryId = @categoryId,
            SupplierId = @supplierId,
            Description = @description,
            PurchasePrice = @purchasePrice,
            SalePrice = @salePrice,
            CurrentStock = @currentStock,
            MinimumStock = @minimumStock,
            Speed = @speed,
            BoxPurchasePrice = @boxPurchasePrice,
            BoxSalePrice = @boxSalePrice,
            ItemsPerBox = @itemsPerBox,
            MinimumBoxStock = @minimumBoxStock,
            IsActive = @isActive,
            UpdatedAt = @updatedAt
        WHERE MainStockId = @mainStockId";

            var parameters = new[]
            {
        new Microsoft.Data.SqlClient.SqlParameter("@mainStockId", item.MainStockId),
        new Microsoft.Data.SqlClient.SqlParameter("@name", item.Name),
        new Microsoft.Data.SqlClient.SqlParameter("@barcode", item.Barcode),
        new Microsoft.Data.SqlClient.SqlParameter("@boxBarcode", (object)item.BoxBarcode ?? DBNull.Value),
        new Microsoft.Data.SqlClient.SqlParameter("@categoryId", item.CategoryId),
        new Microsoft.Data.SqlClient.SqlParameter("@supplierId", (object)item.SupplierId ?? DBNull.Value),
        new Microsoft.Data.SqlClient.SqlParameter("@description", (object)item.Description ?? DBNull.Value),
        new Microsoft.Data.SqlClient.SqlParameter("@purchasePrice", item.PurchasePrice),
        new Microsoft.Data.SqlClient.SqlParameter("@salePrice", item.SalePrice),
        new Microsoft.Data.SqlClient.SqlParameter("@currentStock", item.CurrentStock),
        new Microsoft.Data.SqlClient.SqlParameter("@minimumStock", item.MinimumStock),
        new Microsoft.Data.SqlClient.SqlParameter("@speed", (object)item.Speed ?? DBNull.Value),
        new Microsoft.Data.SqlClient.SqlParameter("@boxPurchasePrice", item.BoxPurchasePrice),
        new Microsoft.Data.SqlClient.SqlParameter("@boxSalePrice", item.BoxSalePrice),
        new Microsoft.Data.SqlClient.SqlParameter("@itemsPerBox", item.ItemsPerBox),
        new Microsoft.Data.SqlClient.SqlParameter("@minimumBoxStock", item.MinimumBoxStock),
        new Microsoft.Data.SqlClient.SqlParameter("@isActive", item.IsActive),
        new Microsoft.Data.SqlClient.SqlParameter("@updatedAt", DateTime.Now)
    };

            await _unitOfWork.Context.Database.ExecuteSqlRawAsync(sql, parameters);
        }

        private async Task SyncLinkedProductsForMainStock(int mainStockId, MainStockDTO mainStockData)
        {
            try
            {
                // Use SQL to find linked products
                string findSql = "SELECT ProductId FROM Products WHERE MainStockId = @mainStockId";
                var findParam = new Microsoft.Data.SqlClient.SqlParameter("@mainStockId", mainStockId);

                var productIds = await _unitOfWork.Context.Set<Product>()
                    .FromSqlRaw(findSql, findParam)
                    .Select(p => p.ProductId)
                    .ToListAsync();

                if (productIds.Any())
                {
                    Debug.WriteLine($"Syncing {productIds.Count} products linked to MainStock {mainStockId}");

                    // Use direct SQL update to update all linked products at once
                    string updateSql = @"
                UPDATE Products
                SET PurchasePrice = @purchasePrice,
                    WholesalePrice = @wholesalePrice,
                    SalePrice = @salePrice,
                    BoxPurchasePrice = @boxPurchasePrice,
                    BoxWholesalePrice = @boxWholesalePrice,                  
                    BoxSalePrice = @boxSalePrice,
                    ItemsPerBox = @itemsPerBox,
                    UpdatedAt = @updatedAt
                WHERE MainStockId = @mainStockId";

                    var updateParams = new[]
                    {
                new Microsoft.Data.SqlClient.SqlParameter("@mainStockId", mainStockId),
                new Microsoft.Data.SqlClient.SqlParameter("@purchasePrice", mainStockData.PurchasePrice),
                new Microsoft.Data.SqlClient.SqlParameter("@wholesalePrice", mainStockData.WholesalePrice),
                new Microsoft.Data.SqlClient.SqlParameter("@salePrice", mainStockData.SalePrice),
                new Microsoft.Data.SqlClient.SqlParameter("@boxPurchasePrice", mainStockData.BoxPurchasePrice),
                new Microsoft.Data.SqlClient.SqlParameter("@boxWholesalePrice", mainStockData.BoxWholesalePrice),
                new Microsoft.Data.SqlClient.SqlParameter("@boxSalePrice", mainStockData.BoxSalePrice),
                new Microsoft.Data.SqlClient.SqlParameter("@itemsPerBox", mainStockData.ItemsPerBox),
                new Microsoft.Data.SqlClient.SqlParameter("@updatedAt", DateTime.Now)
            };

                    await _unitOfWork.Context.Database.ExecuteSqlRawAsync(updateSql, updateParams);

                    // Publish product update events
                    foreach (var productId in productIds)
                    {
                        // Use SparseProdutDTO to avoid tracking issues
                        var updateEvent = new ProductStockUpdatedEvent(productId, mainStockData.CurrentStock);
                        _eventAggregator.Publish(updateEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error syncing linked products: {ex.Message}");
                // Log but don't throw to allow the rest of the batch to continue
            }
        }

        // in QuickTechSystems.Application.Services/MainStockService.cs
        public async Task<bool> TransferToStoreAsync(int mainStockId, int productId, decimal quantity, string transferredBy, string notes, bool isByBoxes = false)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    Debug.WriteLine($"Transfer details: MainStock ID: {mainStockId}, Product ID: {productId}, Quantity: {quantity}, By Boxes: {isByBoxes}");

                    // Get MainStock and Product data to ensure price synchronization
                    var mainStock = await _unitOfWork.MainStocks
                        .Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.MainStockId == mainStockId);

                    if (mainStock == null)
                    {
                        Debug.WriteLine($"MainStock with ID {mainStockId} not found");
                        return false;
                    }

                    var product = await _unitOfWork.Products
                        .Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.ProductId == productId);

                    if (product == null)
                    {
                        Debug.WriteLine($"Product with ID {productId} not found");
                        return false;
                    }

                    // Ensure prices are in sync before transferring
                    bool pricesSynchronized = false;
                    if (Math.Abs(product.WholesalePrice - mainStock.WholesalePrice) > 0.001m ||
                        Math.Abs(product.BoxWholesalePrice - mainStock.BoxWholesalePrice) > 0.001m ||
                        Math.Abs(product.PurchasePrice - mainStock.PurchasePrice) > 0.001m ||
                        Math.Abs(product.SalePrice - mainStock.SalePrice) > 0.001m ||
                        Math.Abs(product.BoxPurchasePrice - mainStock.BoxPurchasePrice) > 0.001m ||
                        Math.Abs(product.BoxSalePrice - mainStock.BoxSalePrice) > 0.001m ||
                        product.ItemsPerBox != mainStock.ItemsPerBox)
                    {
                        // Prices need synchronization - update product with MainStock prices
                        Debug.WriteLine("Synchronizing prices before transfer...");

                        // Create a detached copy to avoid tracking conflicts
                        var productToUpdate = new Product
                        {
                            ProductId = product.ProductId,
                            PurchasePrice = mainStock.PurchasePrice,
                            WholesalePrice = mainStock.WholesalePrice,
                            SalePrice = mainStock.SalePrice,
                            BoxPurchasePrice = mainStock.BoxPurchasePrice,
                            BoxWholesalePrice = mainStock.BoxWholesalePrice,
                            BoxSalePrice = mainStock.BoxSalePrice,
                            ItemsPerBox = mainStock.ItemsPerBox,
                            UpdatedAt = DateTime.Now
                        };

                        // Use raw SQL to update just the price fields to avoid conflicts
                        string sql = @"
                    UPDATE Products 
                    SET PurchasePrice = @purchasePrice,
                        WholesalePrice = @wholesalePrice,
                        SalePrice = @salePrice,
                        BoxPurchasePrice = @boxPurchasePrice,
                        BoxWholesalePrice = @boxWholesalePrice,
                        BoxSalePrice = @boxSalePrice,
                        ItemsPerBox = @itemsPerBox,
                        UpdatedAt = @updatedAt
                    WHERE ProductId = @productId";

                        var parameters = new[]
                        {
                    new Microsoft.Data.SqlClient.SqlParameter("@productId", productToUpdate.ProductId),
                    new Microsoft.Data.SqlClient.SqlParameter("@purchasePrice", productToUpdate.PurchasePrice),
                    new Microsoft.Data.SqlClient.SqlParameter("@wholesalePrice", productToUpdate.WholesalePrice),
                    new Microsoft.Data.SqlClient.SqlParameter("@salePrice", productToUpdate.SalePrice),
                    new Microsoft.Data.SqlClient.SqlParameter("@boxPurchasePrice", productToUpdate.BoxPurchasePrice),
                    new Microsoft.Data.SqlClient.SqlParameter("@boxWholesalePrice", productToUpdate.BoxWholesalePrice),
                    new Microsoft.Data.SqlClient.SqlParameter("@boxSalePrice", productToUpdate.BoxSalePrice),
                    new Microsoft.Data.SqlClient.SqlParameter("@itemsPerBox", productToUpdate.ItemsPerBox),
                    new Microsoft.Data.SqlClient.SqlParameter("@updatedAt", productToUpdate.UpdatedAt)
                };

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(sql, parameters);
                        pricesSynchronized = true;
                    }

                    // Use database connection directly to avoid EF Core tracking completely
                    var connection = _unitOfWork.Context.Database.GetDbConnection();
                    if (connection.State != System.Data.ConnectionState.Open)
                        await connection.OpenAsync();

                    // Step 1: Verify MainStock exists and has sufficient inventory
                    decimal mainStockCurrentStock = 0;
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction.GetDbTransaction();
                        command.CommandText = @"
                SELECT CurrentStock FROM MainStocks 
                WHERE MainStockId = @mainStockId";

                        var param = command.CreateParameter();
                        param.ParameterName = "@mainStockId";
                        param.Value = mainStockId;
                        command.Parameters.Add(param);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                mainStockCurrentStock = reader.GetDecimal(0);
                            }
                            else
                            {
                                // MainStock not found
                                return false;
                            }
                        }
                    }

                    // Check if there's enough stock
                    if (mainStockCurrentStock < quantity)
                    {
                        Debug.WriteLine($"Insufficient stock in MainStock {mainStockId}: {mainStockCurrentStock} < {quantity}");
                        return false;
                    }

                    // Step 2: Update MainStock - deduct inventory
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction.GetDbTransaction();
                        command.CommandText = @"
                UPDATE MainStocks 
                SET CurrentStock = CurrentStock - @quantity, 
                    UpdatedAt = @updatedAt
                WHERE MainStockId = @mainStockId";

                        var idParam = command.CreateParameter();
                        idParam.ParameterName = "@mainStockId";
                        idParam.Value = mainStockId;
                        command.Parameters.Add(idParam);

                        var quantityParam = command.CreateParameter();
                        quantityParam.ParameterName = "@quantity";
                        quantityParam.Value = quantity;
                        command.Parameters.Add(quantityParam);

                        var dateParam = command.CreateParameter();
                        dateParam.ParameterName = "@updatedAt";
                        dateParam.Value = DateTime.Now;
                        command.Parameters.Add(dateParam);

                        await command.ExecuteNonQueryAsync();
                    }

                    // Step 3: Create transfer record
                    string referenceNumber = $"TRF-{DateTime.Now:yyyyMMddHHmmss}-{mainStockId}-{productId}";
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction.GetDbTransaction();
                        command.CommandText = @"
                INSERT INTO InventoryTransfers (
                    MainStockId, ProductId, Quantity, TransferDate, Notes, ReferenceNumber, TransferredBy
                ) VALUES (
                    @mainStockId, @productId, @quantity, @transferDate, @notes, @referenceNumber, @transferredBy
                )";

                        var mainStockIdParam = command.CreateParameter();
                        mainStockIdParam.ParameterName = "@mainStockId";
                        mainStockIdParam.Value = mainStockId;
                        command.Parameters.Add(mainStockIdParam);

                        var productIdParam = command.CreateParameter();
                        productIdParam.ParameterName = "@productId";
                        productIdParam.Value = productId;
                        command.Parameters.Add(productIdParam);

                        var quantityParam = command.CreateParameter();
                        quantityParam.ParameterName = "@quantity";
                        quantityParam.Value = quantity;
                        command.Parameters.Add(quantityParam);

                        var dateParam = command.CreateParameter();
                        dateParam.ParameterName = "@transferDate";
                        dateParam.Value = DateTime.Now;
                        command.Parameters.Add(dateParam);

                        var notesParam = command.CreateParameter();
                        notesParam.ParameterName = "@notes";
                        notesParam.Value = notes ?? "";
                        command.Parameters.Add(notesParam);

                        var refParam = command.CreateParameter();
                        refParam.ParameterName = "@referenceNumber";
                        refParam.Value = referenceNumber;
                        command.Parameters.Add(refParam);

                        var byParam = command.CreateParameter();
                        byParam.ParameterName = "@transferredBy";
                        byParam.Value = transferredBy;
                        command.Parameters.Add(byParam);

                        await command.ExecuteNonQueryAsync();
                    }

                    // Step 4: Update product stock and NumberOfBoxes if transferring by boxes
                    decimal newProductStock = 0;
                    using (var command = connection.CreateCommand())
                    {
                        string boxUpdateClause = isByBoxes ? ", NumberOfBoxes = NumberOfBoxes + @boxQuantity" : "";

                        command.Transaction = transaction.GetDbTransaction();
                        command.CommandText = $@"
                UPDATE Products 
                SET CurrentStock = CurrentStock + @quantity 
                    {boxUpdateClause}, 
                    UpdatedAt = @updatedAt
                WHERE ProductId = @productId;
                
                SELECT CurrentStock, NumberOfBoxes FROM Products WHERE ProductId = @productId;";

                        var idParam = command.CreateParameter();
                        idParam.ParameterName = "@productId";
                        idParam.Value = productId;
                        command.Parameters.Add(idParam);

                        var quantityParam = command.CreateParameter();
                        quantityParam.ParameterName = "@quantity";
                        quantityParam.Value = quantity;
                        command.Parameters.Add(quantityParam);

                        if (isByBoxes)
                        {
                            var boxQuantityParam = command.CreateParameter();
                            boxQuantityParam.ParameterName = "@boxQuantity";
                            boxQuantityParam.Value = (int)quantity / mainStock.ItemsPerBox;
                            command.Parameters.Add(boxQuantityParam);
                        }

                        var dateParam = command.CreateParameter();
                        dateParam.ParameterName = "@updatedAt";
                        dateParam.Value = DateTime.Now;
                        command.Parameters.Add(dateParam);

                        int numberOfBoxes = 0;
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                newProductStock = reader.GetDecimal(0);
                                numberOfBoxes = reader.GetInt32(1);
                                Debug.WriteLine($"Updated product stock to {newProductStock} and boxes to {numberOfBoxes}");
                            }
                        }
                    }

                    // Step 5: Create inventory history record
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction.GetDbTransaction();
                        command.CommandText = @"
                INSERT INTO InventoryHistories (
                    ProductId, QuantityChange, NewQuantity, Type, Notes, Timestamp
                ) VALUES (
                    @productId, @quantityChange, @newQuantity, @type, @notes, @timestamp
                )";

                        var productIdParam = command.CreateParameter();
                        productIdParam.ParameterName = "@productId";
                        productIdParam.Value = productId;
                        command.Parameters.Add(productIdParam);

                        var quantityChangeParam = command.CreateParameter();
                        quantityChangeParam.ParameterName = "@quantityChange";
                        quantityChangeParam.Value = quantity;
                        command.Parameters.Add(quantityChangeParam);

                        var newQuantityParam = command.CreateParameter();
                        newQuantityParam.ParameterName = "@newQuantity";
                        newQuantityParam.Value = newProductStock;
                        command.Parameters.Add(newQuantityParam);

                        var typeParam = command.CreateParameter();
                        typeParam.ParameterName = "@type";
                        typeParam.Value = isByBoxes ? "Transfer-Box" : "Transfer-In";
                        command.Parameters.Add(typeParam);

                        var notesParam = command.CreateParameter();
                        notesParam.ParameterName = "@notes";
                        notesParam.Value = $"Transfer from MainStock ID: {mainStockId}";
                        if (isByBoxes)
                        {
                            notesParam.Value = $"Box Transfer from MainStock ID: {mainStockId}";
                        }
                        command.Parameters.Add(notesParam);

                        var timestampParam = command.CreateParameter();
                        timestampParam.ParameterName = "@timestamp";
                        timestampParam.Value = DateTime.Now;
                        command.Parameters.Add(timestampParam);

                        await command.ExecuteNonQueryAsync();
                    }

                    // Commit the transaction
                    await transaction.CommitAsync();

                    // Publish events after transaction completes
                    // Get fresh data without tracking for the event payloads
                    var mainStockDto = await GetMainStockDtoForEvent(mainStockId);
                    if (mainStockDto != null)
                    {
                        _eventAggregator.Publish(new EntityChangedEvent<MainStockDTO>("Update", mainStockDto));
                    }

                    // Publish product stock update event
                    _eventAggregator.Publish(new ProductStockUpdatedEvent(productId, newProductStock));

                    // If prices were synchronized, publish a product update event too
                    if (pricesSynchronized)
                    {
                        var updatedProduct = await _unitOfWork.Products
                            .Query()
                            .AsNoTracking()
                            .FirstOrDefaultAsync(p => p.ProductId == productId);

                        if (updatedProduct != null)
                        {
                            var productDto = _mapper.Map<ProductDTO>(updatedProduct);
                            _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", productDto));
                        }
                    }

                    Debug.WriteLine($"Successfully transferred {quantity} units from MainStock {mainStockId} to Product {productId}");
                    return true;
                }
                catch (Exception ex)
                {
                    // Ensure transaction is rolled back
                    await transaction.RollbackAsync();

                    // Log the detailed error
                    Debug.WriteLine($"Error in inventory transfer: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }

                    // Re-throw with appropriate message
                    throw new InvalidOperationException($"Transfer failed: {ex.Message}", ex);
                }
            });
        }


        // Helper method to get MainStock data for event publishing
        private async Task<MainStockDTO> GetMainStockDtoForEvent(int mainStockId)
        {
            var mainStock = await _unitOfWork.Context.Set<MainStock>()
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.MainStockId == mainStockId);

            return mainStock != null ? _mapper.Map<MainStockDTO>(mainStock) : null;
        }
        // New helper method for direct stock updates to avoid tracking conflicts
        private async Task<bool> UpdateProductStockDirectly(int productId, decimal quantityToAdd, string notes)
        {
            try
            {
                // Use a direct SQL update to avoid entity tracking issues
                // This is a simplified example - adjust based on your actual database schema
                string sql = @"
            UPDATE Products 
            SET CurrentStock = CurrentStock + @quantity, 
                UpdatedAt = @updatedAt
            WHERE ProductId = @productId;
            
            INSERT INTO InventoryHistories (
                ProductId, QuantityChange, NewQuantity, Type, Notes, Timestamp
            )
            SELECT 
                @productId, 
                @quantity, 
                (SELECT CurrentStock FROM Products WHERE ProductId = @productId), 
                'Transfer-In', 
                @notes, 
                @timestamp;";

                var parameters = new[]
                {
            new Microsoft.Data.SqlClient.SqlParameter("@productId", productId),
            new Microsoft.Data.SqlClient.SqlParameter("@quantity", quantityToAdd),
            new Microsoft.Data.SqlClient.SqlParameter("@updatedAt", DateTime.Now),
            new Microsoft.Data.SqlClient.SqlParameter("@notes", notes),
            new Microsoft.Data.SqlClient.SqlParameter("@timestamp", DateTime.Now)
        };

                await _unitOfWork.Context.Database.ExecuteSqlRawAsync(sql, parameters);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error directly updating product stock: {ex.Message}");
                return false;
            }
        }

        // Helper method to get the current product stock
        private async Task<decimal> GetCurrentProductStock(int productId)
        {
            var product = await _unitOfWork.Products.GetByIdAsync(productId);
            return product?.CurrentStock ?? 0;
        }

        // Update the UpdateAsync method to trigger product synchronization
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

                    // Find linked products and update them - this improves synchronization
                    var linkedProducts = await _unitOfWork.Products
                        .Query()
                        .Where(p => p.MainStockId == dto.MainStockId)
                        .ToListAsync();

                    if (linkedProducts.Any())
                    {
                        foreach (var product in linkedProducts)
                        {
                            // Update prices
                            product.PurchasePrice = dto.PurchasePrice;
                            product.SalePrice = dto.SalePrice;
                            product.BoxPurchasePrice = dto.BoxPurchasePrice;
                            product.BoxSalePrice = dto.BoxSalePrice;
                            product.ItemsPerBox = dto.ItemsPerBox;
                            product.UpdatedAt = DateTime.Now;
                            product.WholesalePrice = dto.WholesalePrice;
                            product.BoxWholesalePrice = dto.BoxWholesalePrice;


                            await _unitOfWork.Products.UpdateAsync(product);

                            // Publish product update event
                            var productDto = _mapper.Map<ProductDTO>(product);
                            _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", productDto));
                        }

                        await _unitOfWork.SaveChangesAsync();
                        Debug.WriteLine($"Updated {linkedProducts.Count} linked products with MainStock data");
                    }

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
                        p.BoxBarcode.ToLower().Contains(searchTerm) ||
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