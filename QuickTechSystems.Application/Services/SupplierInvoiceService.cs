using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using AutoMapper;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces;

namespace QuickTechSystems.Application.Services
{
    public class SupplierInvoiceService : ISupplierInvoiceService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDbContextScopeService _dbContextScopeService;
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _invoiceLocks = new();

        public SupplierInvoiceService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dbContextScopeService = dbContextScopeService ?? throw new ArgumentNullException(nameof(dbContextScopeService));
        }

        private async Task<SemaphoreSlim> GetInvoiceLock(int invoiceId)
        {
            return _invoiceLocks.GetOrAdd(invoiceId, _ => new SemaphoreSlim(1, 1));
        }

        public async Task<IEnumerable<SupplierInvoiceDTO>> GetAllAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var invoices = await _unitOfWork.SupplierInvoices.Query()
                    .Include(si => si.Supplier)
                    .Include(si => si.Details)
                        .ThenInclude(d => d.Product)
                            .ThenInclude(p => p.Category)
                    .OrderByDescending(si => si.CreatedAt)
                    .AsNoTracking()
                    .ToListAsync();

                var dtos = _mapper.Map<IEnumerable<SupplierInvoiceDTO>>(invoices);
                foreach (var dto in dtos)
                {
                    await RecalculateInvoiceAmounts(dto);
                }
                return dtos;
            });
        }

        public async Task<IEnumerable<SupplierInvoiceDTO>> GetBySupplierAsync(int supplierId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var invoices = await _unitOfWork.SupplierInvoices.Query()
                    .Include(si => si.Supplier)
                    .Include(si => si.Details)
                        .ThenInclude(d => d.Product)
                            .ThenInclude(p => p.Category)
                    .Where(si => si.SupplierId == supplierId)
                    .OrderByDescending(si => si.CreatedAt)
                    .AsNoTracking()
                    .ToListAsync();

                var dtos = _mapper.Map<IEnumerable<SupplierInvoiceDTO>>(invoices);
                foreach (var dto in dtos)
                {
                    await RecalculateInvoiceAmounts(dto);
                }
                return dtos;
            });
        }

        public async Task<IEnumerable<SupplierInvoiceDTO>> GetRecentInvoicesAsync(DateTime startDate)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var invoices = await _unitOfWork.SupplierInvoices.Query()
                    .Include(si => si.Supplier)
                    .Include(si => si.Details)
                        .ThenInclude(d => d.Product)
                            .ThenInclude(p => p.Category)
                    .Where(si => si.CreatedAt >= startDate)
                    .OrderByDescending(si => si.CreatedAt)
                    .AsNoTracking()
                    .ToListAsync();

                var dtos = _mapper.Map<IEnumerable<SupplierInvoiceDTO>>(invoices);
                foreach (var dto in dtos)
                {
                    await RecalculateInvoiceAmounts(dto);
                }
                return dtos;
            });
        }

        public async Task<SupplierInvoiceDTO?> GetByIdAsync(int invoiceId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var invoice = await _unitOfWork.SupplierInvoices.Query()
                    .Include(si => si.Supplier)
                    .Include(si => si.Details)
                        .ThenInclude(d => d.Product)
                            .ThenInclude(p => p.Category)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(si => si.SupplierInvoiceId == invoiceId);

                if (invoice == null) return null;

                var dto = _mapper.Map<SupplierInvoiceDTO>(invoice);
                await RecalculateInvoiceAmounts(dto);

                return dto;
            });
        }

        private async Task RecalculateInvoiceAmounts(SupplierInvoiceDTO dto)
        {
            decimal calculatedTotal = 0;

            if (dto.Details?.Count > 0)
            {
                foreach (var detail in dto.Details)
                {
                    var detailTotal = detail.Quantity * detail.PurchasePrice;
                    detail.TotalPrice = detailTotal;
                    calculatedTotal += detailTotal;
                }
            }

            dto.CalculatedAmount = calculatedTotal;
        }

        public async Task<SupplierInvoiceDTO> CreateAsync(SupplierInvoiceDTO invoiceDto)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var invoice = _mapper.Map<SupplierInvoice>(invoiceDto);
                    invoice.CreatedAt = DateTime.Now;
                    invoice.CalculatedAmount = 0;

                    var result = await _unitOfWork.SupplierInvoices.AddAsync(invoice);
                    await _unitOfWork.SaveChangesAsync();

                    result = await _unitOfWork.SupplierInvoices.Query()
                        .Include(si => si.Supplier)
                        .Include(si => si.Details)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(si => si.SupplierInvoiceId == result.SupplierInvoiceId);

                    await transaction.CommitAsync();

                    var resultDto = _mapper.Map<SupplierInvoiceDTO>(result);
                    await RecalculateInvoiceAmounts(resultDto);

                    _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Create", resultDto));

                    return resultDto;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine($"[CreateAsync] ERROR: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task UpdateAsync(SupplierInvoiceDTO invoiceDto)
        {
            var invoiceLock = await GetInvoiceLock(invoiceDto.SupplierInvoiceId);
            await invoiceLock.WaitAsync();
            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var existingInvoice = await _unitOfWork.SupplierInvoices.GetByIdAsync(invoiceDto.SupplierInvoiceId);
                        if (existingInvoice == null)
                        {
                            throw new InvalidOperationException($"Invoice with ID {invoiceDto.SupplierInvoiceId} not found");
                        }

                        _mapper.Map(invoiceDto, existingInvoice);
                        existingInvoice.UpdatedAt = DateTime.Now;

                        await _unitOfWork.SupplierInvoices.UpdateAsync(existingInvoice);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        await RecalculateInvoiceAmounts(invoiceDto);
                        _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Update", invoiceDto));
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"[UpdateAsync] ERROR: {ex.Message}");
                        throw;
                    }
                });
            }
            finally
            {
                invoiceLock.Release();
            }
        }

        public async Task DeleteAsync(int invoiceId)
        {
            var invoiceLock = await GetInvoiceLock(invoiceId);
            await invoiceLock.WaitAsync();
            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var invoice = await _unitOfWork.SupplierInvoices.Query()
                            .Include(si => si.Details)
                            .FirstOrDefaultAsync(si => si.SupplierInvoiceId == invoiceId);

                        if (invoice == null)
                        {
                            throw new InvalidOperationException($"Invoice with ID {invoiceId} not found");
                        }

                        if (invoice.Status != "Draft")
                        {
                            throw new InvalidOperationException($"Cannot delete invoice in {invoice.Status} status");
                        }

                        foreach (var detail in invoice.Details)
                        {
                            var product = await _unitOfWork.Products.GetByIdAsync(detail.ProductId);
                            if (product != null)
                            {
                                product.CurrentStock -= (int)detail.Quantity;
                                product.UpdatedAt = DateTime.Now;
                                await _unitOfWork.Products.UpdateAsync(product);
                            }
                        }

                        await _unitOfWork.SupplierInvoices.DeleteAsync(invoice);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Delete", new SupplierInvoiceDTO { SupplierInvoiceId = invoiceId }));
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"[DeleteAsync] ERROR: {ex.Message}");
                        throw;
                    }
                });
            }
            finally
            {
                invoiceLock.Release();
            }
        }

        public async Task<IEnumerable<SupplierInvoiceDTO>> GetByStatusAsync(string status)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var invoices = await _unitOfWork.SupplierInvoices.Query()
                    .Include(si => si.Supplier)
                    .Include(si => si.Details)
                        .ThenInclude(d => d.Product)
                            .ThenInclude(p => p.Category)
                    .Where(si => si.Status == status)
                    .OrderByDescending(si => si.CreatedAt)
                    .AsNoTracking()
                    .ToListAsync();

                var dtos = _mapper.Map<IEnumerable<SupplierInvoiceDTO>>(invoices);
                foreach (var dto in dtos)
                {
                    await RecalculateInvoiceAmounts(dto);
                }
                return dtos;
            });
        }

        public async Task<bool> ValidateInvoiceAsync(int invoiceId)
        {
            var invoiceLock = await GetInvoiceLock(invoiceId);
            await invoiceLock.WaitAsync();
            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var invoice = await _unitOfWork.SupplierInvoices.GetByIdAsync(invoiceId);
                        if (invoice == null)
                        {
                            throw new InvalidOperationException($"Invoice with ID {invoiceId} not found");
                        }

                        if (invoice.Status != "Draft")
                        {
                            throw new InvalidOperationException($"Cannot validate invoice in {invoice.Status} status");
                        }

                        invoice.Status = "Validated";
                        invoice.UpdatedAt = DateTime.Now;

                        await _unitOfWork.SupplierInvoices.UpdateAsync(invoice);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        var updatedInvoice = await GetByIdAsync(invoiceId);
                        if (updatedInvoice != null)
                        {
                            _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Update", updatedInvoice));
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"[ValidateInvoiceAsync] ERROR: {ex.Message}");
                        throw;
                    }
                });
            }
            finally
            {
                invoiceLock.Release();
            }
        }

        public async Task<bool> SettleInvoiceAsync(int invoiceId, decimal paymentAmount = 0)
        {
            var invoiceLock = await GetInvoiceLock(invoiceId);
            await invoiceLock.WaitAsync();
            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var invoice = await _unitOfWork.SupplierInvoices.Query()
                            .Include(si => si.Supplier)
                            .FirstOrDefaultAsync(si => si.SupplierInvoiceId == invoiceId);

                        if (invoice == null)
                        {
                            throw new InvalidOperationException($"Invoice with ID {invoiceId} not found");
                        }

                        if (invoice.Status == "Settled")
                        {
                            throw new InvalidOperationException("Invoice is already settled");
                        }

                        invoice.Status = "Settled";
                        invoice.UpdatedAt = DateTime.Now;

                        await _unitOfWork.SupplierInvoices.UpdateAsync(invoice);

                        if (paymentAmount > 0 && invoice.Supplier != null)
                        {
                            var supplierTransaction = new SupplierTransaction
                            {
                                SupplierId = invoice.SupplierId,
                                Amount = paymentAmount,
                                TransactionType = "Payment",
                                Reference = $"Settlement for Invoice {invoice.InvoiceNumber}",
                                TransactionDate = DateTime.Now,
                                Notes = $"Payment for supplier invoice {invoice.InvoiceNumber}"
                            };

                            await _unitOfWork.Context.Set<SupplierTransaction>().AddAsync(supplierTransaction);

                            invoice.Supplier.Balance -= paymentAmount;
                            await _unitOfWork.Suppliers.UpdateAsync(invoice.Supplier);
                        }

                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        var updatedInvoice = await GetByIdAsync(invoiceId);
                        if (updatedInvoice != null)
                        {
                            _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Update", updatedInvoice));
                        }

                        _eventAggregator.Publish(new EntityChangedEvent<SupplierDTO>("Update", new SupplierDTO
                        {
                            SupplierId = invoice.SupplierId,
                            Name = invoice.Supplier?.Name ?? "Unknown Supplier"
                        }));

                        return true;
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"[SettleInvoiceAsync] ERROR: {ex.Message}");
                        throw;
                    }
                });
            }
            finally
            {
                invoiceLock.Release();
            }
        }

        public async Task<IEnumerable<SupplierTransactionDTO>> GetInvoicePaymentsAsync(int invoiceId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var invoice = await _unitOfWork.SupplierInvoices.GetByIdAsync(invoiceId);
                if (invoice == null)
                {
                    throw new InvalidOperationException($"Invoice with ID {invoiceId} not found");
                }

                var payments = await _unitOfWork.Context.Set<SupplierTransaction>()
                    .Include(st => st.Supplier)
                    .Where(st => st.Reference != null && st.Reference.Contains($"INV-{invoice.InvoiceNumber}"))
                    .OrderByDescending(st => st.TransactionDate)
                    .AsNoTracking()
                    .ToListAsync();

                return _mapper.Map<IEnumerable<SupplierTransactionDTO>>(payments);
            });
        }

        public async Task<IEnumerable<string>> GetInvoiceNumbersForAutocompleteAsync(string searchTerm)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                return await _unitOfWork.SupplierInvoices.Query()
                    .Where(si => si.Status == "Draft" && si.InvoiceNumber.Contains(searchTerm))
                    .Select(si => si.InvoiceNumber)
                    .Take(10)
                    .AsNoTracking()
                    .ToListAsync();
            });
        }

        public async Task<SupplierInvoiceDTO?> GetByInvoiceNumberAsync(string invoiceNumber, int supplierId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var invoice = await _unitOfWork.SupplierInvoices.Query()
                    .Include(si => si.Supplier)
                    .Include(si => si.Details)
                        .ThenInclude(d => d.Product)
                            .ThenInclude(p => p.Category)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(si => si.InvoiceNumber == invoiceNumber && si.SupplierId == supplierId);

                if (invoice == null) return null;

                var dto = _mapper.Map<SupplierInvoiceDTO>(invoice);
                await RecalculateInvoiceAmounts(dto);

                return dto;
            });
        }

        private async Task UpdateCalculatedAmountDirectly(int invoiceId)
        {
            try
            {
                var detailTotals = await _unitOfWork.Context.Set<SupplierInvoiceDetail>()
                    .Where(d => d.SupplierInvoiceId == invoiceId)
                    .Select(d => new { d.Quantity, d.PurchasePrice })
                    .AsNoTracking()
                    .ToListAsync();

                decimal calculatedAmount = detailTotals.Sum(d => d.Quantity * d.PurchasePrice);

                var updateParameters = new[]
                {
                    new SqlParameter("@calculatedAmount", calculatedAmount),
                    new SqlParameter("@updatedAt", DateTime.Now),
                    new SqlParameter("@invoiceId", invoiceId)
                };

                string updateSql = @"
                    UPDATE SupplierInvoices 
                    SET CalculatedAmount = @calculatedAmount, UpdatedAt = @updatedAt 
                    WHERE SupplierInvoiceId = @invoiceId";

                await _unitOfWork.Context.Database.ExecuteSqlRawAsync(updateSql, updateParameters);

                Debug.WriteLine($"[UpdateCalculatedAmountDirectly] Updated invoice {invoiceId} with calculated amount: {calculatedAmount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateCalculatedAmountDirectly] ERROR: {ex.Message}");
                throw;
            }
        }

        public async Task UpdateCalculatedAmountAsync(int invoiceId)
        {
            var invoiceLock = await GetInvoiceLock(invoiceId);
            await invoiceLock.WaitAsync();
            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    await UpdateCalculatedAmountDirectly(invoiceId);

                    var updatedInvoice = await GetByIdAsync(invoiceId);
                    if (updatedInvoice != null)
                    {
                        _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Update", updatedInvoice));
                    }
                });
            }
            finally
            {
                invoiceLock.Release();
            }
        }

        public async Task AddProductToInvoiceAsync(SupplierInvoiceDetailDTO detailDto)
        {
            var invoiceLock = await GetInvoiceLock(detailDto.SupplierInvoiceId);
            await invoiceLock.WaitAsync();
            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        Debug.WriteLine($"[AddProductToInvoiceAsync] Starting for InvoiceId: {detailDto.SupplierInvoiceId}, ProductId: {detailDto.ProductId}");

                        var invoiceStatus = await _unitOfWork.Context.Set<SupplierInvoice>()
                            .AsNoTracking()
                            .Where(i => i.SupplierInvoiceId == detailDto.SupplierInvoiceId)
                            .Select(i => new { i.Status })
                            .FirstOrDefaultAsync();

                        if (invoiceStatus == null)
                        {
                            throw new InvalidOperationException($"Invoice with ID {detailDto.SupplierInvoiceId} not found");
                        }

                        if (invoiceStatus.Status != "Draft")
                        {
                            throw new InvalidOperationException($"Cannot add products to invoice in {invoiceStatus.Status} status");
                        }

                        var product = await _unitOfWork.Context.Set<Product>()
                            .Include(p => p.Category)
                            .Include(p => p.Supplier)
                            .AsNoTracking()
                            .FirstOrDefaultAsync(p => p.ProductId == detailDto.ProductId);

                        if (product == null)
                        {
                            throw new InvalidOperationException($"Product with ID {detailDto.ProductId} not found");
                        }

                        detailDto.TotalPrice = Math.Round(detailDto.Quantity * detailDto.PurchasePrice, 2);
                        Debug.WriteLine($"[AddProductToInvoiceAsync] Calculated TotalPrice: {detailDto.TotalPrice} (Qty: {detailDto.Quantity} * Price: {detailDto.PurchasePrice})");

                        var detail = new SupplierInvoiceDetail
                        {
                            SupplierInvoiceId = detailDto.SupplierInvoiceId,
                            ProductId = detailDto.ProductId,
                            Quantity = detailDto.Quantity,
                            PurchasePrice = detailDto.PurchasePrice,
                            TotalPrice = detailDto.TotalPrice,
                            BoxBarcode = detailDto.BoxBarcode ?? string.Empty,
                            NumberOfBoxes = detailDto.NumberOfBoxes,
                            ItemsPerBox = detailDto.ItemsPerBox,
                            BoxPurchasePrice = detailDto.BoxPurchasePrice,
                            BoxSalePrice = detailDto.BoxSalePrice,
                            CurrentStock = detailDto.CurrentStock,
                            Storehouse = detailDto.Storehouse,
                            SalePrice = detailDto.SalePrice,
                            WholesalePrice = detailDto.WholesalePrice,
                            BoxWholesalePrice = detailDto.BoxWholesalePrice,
                            MinimumStock = detailDto.MinimumStock
                        };

                        await _unitOfWork.SupplierInvoiceDetails.AddAsync(detail);
                        await _unitOfWork.SaveChangesAsync();

                        Debug.WriteLine($"[AddProductToInvoiceAsync] Detail added successfully with TotalPrice: {detail.TotalPrice}");

                        await UpdateCalculatedAmountDirectly(detailDto.SupplierInvoiceId);

                        await transaction.CommitAsync();
                        Debug.WriteLine($"[AddProductToInvoiceAsync] Transaction committed successfully");

                        var updatedInvoice = await GetByIdAsync(detailDto.SupplierInvoiceId);
                        if (updatedInvoice != null)
                        {
                            Debug.WriteLine($"[AddProductToInvoiceAsync] Final CalculatedAmount: {updatedInvoice.CalculatedAmount}");
                            _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Update", updatedInvoice));
                        }
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"[AddProductToInvoiceAsync] ERROR: {ex}");
                        throw;
                    }
                });
            }
            finally
            {
                invoiceLock.Release();
            }
        }

        public async Task RemoveProductFromInvoiceAsync(int detailId)
        {
            await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var detail = await _unitOfWork.SupplierInvoiceDetails.GetByIdAsync(detailId);
                    if (detail == null)
                    {
                        throw new InvalidOperationException($"Invoice detail with ID {detailId} not found");
                    }

                    var invoiceLock = await GetInvoiceLock(detail.SupplierInvoiceId);
                    await invoiceLock.WaitAsync();
                    try
                    {
                        var invoice = await _unitOfWork.SupplierInvoices.GetByIdAsync(detail.SupplierInvoiceId);
                        if (invoice == null)
                        {
                            throw new InvalidOperationException($"Invoice not found");
                        }

                        if (invoice.Status != "Draft")
                        {
                            throw new InvalidOperationException($"Cannot remove products from invoice in {invoice.Status} status");
                        }

                        var product = await _unitOfWork.Products.GetByIdAsync(detail.ProductId);
                        if (product != null)
                        {
                            product.CurrentStock -= (int)detail.Quantity;
                            product.UpdatedAt = DateTime.Now;
                            await _unitOfWork.Products.UpdateAsync(product);

                            var inventoryHistory = new InventoryHistory
                            {
                                ProductId = product.ProductId,
                                QuantityChange = -detail.Quantity,
                                NewQuantity = product.CurrentStock,
                                Type = "Adjustment",
                                Notes = $"Removed from supplier invoice {invoice.InvoiceNumber}",
                                Timestamp = DateTime.Now
                            };

                            await _unitOfWork.Context.Set<InventoryHistory>().AddAsync(inventoryHistory);
                        }

                        await _unitOfWork.SupplierInvoiceDetails.DeleteAsync(detail);
                        await _unitOfWork.SaveChangesAsync();

                        await UpdateCalculatedAmountDirectly(detail.SupplierInvoiceId);

                        await transaction.CommitAsync();

                        if (product != null)
                        {
                            var productDto = _mapper.Map<ProductDTO>(product);
                            _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", productDto));
                        }

                        var updatedInvoice = await GetByIdAsync(detail.SupplierInvoiceId);
                        if (updatedInvoice != null)
                        {
                            _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Update", updatedInvoice));
                        }
                    }
                    finally
                    {
                        invoiceLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine($"[RemoveProductFromInvoiceAsync] ERROR: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<ProductDTO> CreateNewProductAndAddToInvoiceAsync(NewProductFromInvoiceDTO newProductDto, int invoiceId)
        {
            var invoiceLock = await GetInvoiceLock(invoiceId);
            await invoiceLock.WaitAsync();
            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var invoice = await _unitOfWork.SupplierInvoices.Query()
                            .Where(i => i.SupplierInvoiceId == invoiceId)
                            .Select(i => new { i.Status, i.SupplierId })
                            .FirstOrDefaultAsync();

                        if (invoice == null)
                        {
                            throw new InvalidOperationException($"Invoice with ID {invoiceId} not found");
                        }

                        if (invoice.Status != "Draft")
                        {
                            throw new InvalidOperationException($"Cannot add products to invoice in {invoice.Status} status");
                        }

                        var existingProduct = await _unitOfWork.Products.Query()
                            .Where(p => p.Barcode == newProductDto.Barcode)
                            .FirstOrDefaultAsync();

                        if (existingProduct != null)
                        {
                            throw new InvalidOperationException($"A product with barcode '{newProductDto.Barcode}' already exists");
                        }

                        var category = await _unitOfWork.Categories.GetByIdAsync(newProductDto.CategoryId);
                        if (category == null)
                        {
                            throw new InvalidOperationException($"Category with ID {newProductDto.CategoryId} not found");
                        }

                        var supplier = await _unitOfWork.Suppliers.GetByIdAsync(newProductDto.SupplierId);
                        if (supplier == null)
                        {
                            throw new InvalidOperationException($"Supplier with ID {newProductDto.SupplierId} not found");
                        }

                        var product = new Product
                        {
                            Barcode = newProductDto.Barcode,
                            Name = newProductDto.Name,
                            Description = newProductDto.Description,
                            CategoryId = newProductDto.CategoryId,
                            PurchasePrice = newProductDto.PurchasePrice,
                            SalePrice = newProductDto.SalePrice,
                            CurrentStock = newProductDto.CurrentStock,
                            Storehouse = newProductDto.Storehouse,
                            MinimumStock = newProductDto.MinimumStock,
                            SupplierId = newProductDto.SupplierId,
                            IsActive = newProductDto.IsActive,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            ImagePath = newProductDto.ImagePath,
                            BarcodeImage = null,
                            BoxBarcode = newProductDto.BoxBarcode,
                            NumberOfBoxes = newProductDto.NumberOfBoxes,
                            ItemsPerBox = newProductDto.ItemsPerBox,
                            BoxPurchasePrice = newProductDto.BoxPurchasePrice,
                            BoxSalePrice = newProductDto.BoxSalePrice,
                            MinimumBoxStock = newProductDto.MinimumBoxStock,
                            WholesalePrice = newProductDto.WholesalePrice,
                            BoxWholesalePrice = newProductDto.BoxWholesalePrice
                        };

                        await _unitOfWork.Products.AddAsync(product);
                        await _unitOfWork.SaveChangesAsync();

                        Debug.WriteLine($"[CreateNewProductAndAddToInvoiceAsync] Product created with ID: {product.ProductId}");

                        var totalStock = newProductDto.CurrentStock + newProductDto.Storehouse;
                        if (totalStock > 0)
                        {
                            var inventoryHistory = new InventoryHistory
                            {
                                ProductId = product.ProductId,
                                QuantityChange = totalStock,
                                NewQuantity = totalStock,
                                Type = "Initial Stock",
                                Notes = $"Initial stock from supplier invoice (Current: {newProductDto.CurrentStock}, Storehouse: {newProductDto.Storehouse})",
                                Timestamp = DateTime.Now
                            };

                            await _unitOfWork.InventoryHistories.AddAsync(inventoryHistory);
                        }

                        var invoiceDetail = new SupplierInvoiceDetail
                        {
                            SupplierInvoiceId = invoiceId,
                            ProductId = product.ProductId,
                            Quantity = newProductDto.InvoiceQuantity,
                            PurchasePrice = newProductDto.PurchasePrice,
                            TotalPrice = newProductDto.InvoiceTotalPrice,
                            BoxBarcode = newProductDto.BoxBarcode,
                            NumberOfBoxes = newProductDto.NumberOfBoxes,
                            ItemsPerBox = newProductDto.ItemsPerBox,
                            BoxPurchasePrice = newProductDto.BoxPurchasePrice,
                            BoxSalePrice = newProductDto.BoxSalePrice,
                            CurrentStock = newProductDto.CurrentStock,
                            Storehouse = newProductDto.Storehouse,
                            SalePrice = newProductDto.SalePrice,
                            WholesalePrice = newProductDto.WholesalePrice,
                            BoxWholesalePrice = newProductDto.BoxWholesalePrice,
                            MinimumStock = newProductDto.MinimumStock
                        };

                        await _unitOfWork.SupplierInvoiceDetails.AddAsync(invoiceDetail);
                        await _unitOfWork.SaveChangesAsync();

                        Debug.WriteLine($"[CreateNewProductAndAddToInvoiceAsync] Invoice detail added with TotalPrice: {invoiceDetail.TotalPrice}");

                        await UpdateCalculatedAmountDirectly(invoiceId);

                        await transaction.CommitAsync();
                        Debug.WriteLine($"[CreateNewProductAndAddToInvoiceAsync] Transaction committed successfully");

                        var productDto = new ProductDTO
                        {
                            ProductId = product.ProductId,
                            Barcode = product.Barcode,
                            Name = product.Name,
                            Description = product.Description,
                            CategoryId = product.CategoryId,
                            CategoryName = category.Name,
                            PurchasePrice = product.PurchasePrice,
                            SalePrice = product.SalePrice,
                            CurrentStock = product.CurrentStock,
                            Storehouse = product.Storehouse,
                            MinimumStock = product.MinimumStock,
                            SupplierId = product.SupplierId,
                            SupplierName = supplier.Name,
                            IsActive = product.IsActive,
                            CreatedAt = product.CreatedAt,
                            UpdatedAt = product.UpdatedAt,
                            ImagePath = product.ImagePath,
                            BarcodeImage = product.BarcodeImage,
                            BoxBarcode = product.BoxBarcode,
                            NumberOfBoxes = product.NumberOfBoxes,
                            ItemsPerBox = product.ItemsPerBox,
                            BoxPurchasePrice = product.BoxPurchasePrice,
                            BoxSalePrice = product.BoxSalePrice,
                            MinimumBoxStock = product.MinimumBoxStock,
                            WholesalePrice = product.WholesalePrice,
                            BoxWholesalePrice = product.BoxWholesalePrice
                        };

                        _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Create", productDto));

                        var updatedInvoice = await GetByIdAsync(invoiceId);
                        if (updatedInvoice != null)
                        {
                            _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Update", updatedInvoice));
                        }

                        return productDto;
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"[CreateNewProductAndAddToInvoiceAsync] ERROR: {ex}");
                        throw;
                    }
                });
            }
            finally
            {
                invoiceLock.Release();
            }
        }
    }
}