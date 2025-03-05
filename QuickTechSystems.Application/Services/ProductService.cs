// QuickTechSystems.Application/Services/ProductService.cs
using System.Diagnostics;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
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
          IEventAggregator eventAggregator)
          : base(unitOfWork, mapper, unitOfWork.Products, eventAggregator)
        {
        }

        public async Task<IEnumerable<ProductDTO>> GetByCategoryAsync(int categoryId)
        {
            var products = await _repository.Query()
                .Include(p => p.Category)
                .Where(p => p.CategoryId == categoryId)
                .ToListAsync();
            return _mapper.Map<IEnumerable<ProductDTO>>(products);
        }

        public async Task<IEnumerable<ProductDTO>> GetLowStockProductsAsync(int? customThreshold = null)
        {
            var products = await _repository.Query()
                .Include(p => p.Category)
                .Include(p => p.Supplier)
                .Where(p => p.CurrentStock <= (customThreshold ?? p.MinimumStock))
                .ToListAsync();

            return _mapper.Map<IEnumerable<ProductDTO>>(products);
        }

        public async Task<bool> UpdateStockAsync(int productId, int quantity)
        {
            var product = await _repository.GetByIdAsync(productId);
            if (product == null) return false;

            product.CurrentStock += quantity;
            product.UpdatedAt = DateTime.Now;

            await _repository.UpdateAsync(product);
            await _unitOfWork.SaveChangesAsync();

            // Publish the update event to notify subscribers
            var productDto = _mapper.Map<ProductDTO>(product);
            _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", productDto));

            return true;
        }

        public async Task<IEnumerable<ProductDTO>> GetLowStockProductsAsync()
        {
            var products = await _repository.Query()
                .Where(p => p.CurrentStock <= p.MinimumStock)
                .ToListAsync();
            return _mapper.Map<IEnumerable<ProductDTO>>(products);
        }

        public override async Task UpdateAsync(ProductDTO dto)
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
        }

        public override async Task<IEnumerable<ProductDTO>> GetAllAsync()
        {
            var products = await _repository.Query()
                .Include(p => p.Category)
                .Include(p => p.Supplier)
                .ToListAsync();
            return _mapper.Map<IEnumerable<ProductDTO>>(products);
        }

        public async Task<ProductDTO?> GetByBarcodeAsync(string barcode)
        {
            var product = await _repository.Query()
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Barcode == barcode);
            return _mapper.Map<ProductDTO>(product);
        }

        public override async Task<ProductDTO> CreateAsync(ProductDTO dto)
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
        }

        // Override the DeleteAsync method to implement safer deletion with dependency checking
        public override async Task DeleteAsync(int id)
        {
            try
            {
                // Begin transaction for safety
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var product = await _repository.Query()
                        .Include(p => p.TransactionDetails)
                        .Include(p => p.InventoryHistories)
                        .FirstOrDefaultAsync(p => p.ProductId == id);

                    if (product == null)
                    {
                        throw new InvalidOperationException($"Product with ID {id} not found");
                    }

                    // Check if there are any related records that would prevent deletion
                    bool hasTransactionDetails = product.TransactionDetails.Any();
                    bool hasInventoryHistory = product.InventoryHistories.Any();

                    if (hasTransactionDetails || hasInventoryHistory)
                    {
                        // If there are related records, we can't physically delete the product
                        // Perform soft delete instead
                        product.IsActive = false;
                        product.UpdatedAt = DateTime.Now;

                        await _repository.UpdateAsync(product);
                        await _unitOfWork.SaveChangesAsync();

                        var dto = _mapper.Map<ProductDTO>(product);
                        _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", dto));

                        // Throw exception to indicate soft delete was performed
                        throw new InvalidOperationException(
                            "This product has associated transaction records and cannot be physically deleted. " +
                            "It has been marked as inactive instead.");
                    }
                    else
                    {
                        // If no related records, proceed with physical deletion
                        await _repository.DeleteAsync(product);
                        await _unitOfWork.SaveChangesAsync();

                        var dto = _mapper.Map<ProductDTO>(product);
                        _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Delete", dto));
                    }

                    await transaction.CommitAsync();
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in DeleteAsync: {ex}");
                throw;
            }
        }

        // Add a new method for soft delete only
        public async Task SoftDeleteAsync(int id)
        {
            var product = await _repository.GetByIdAsync(id);
            if (product == null)
            {
                throw new InvalidOperationException($"Product with ID {id} not found");
            }

            product.IsActive = false;
            product.UpdatedAt = DateTime.Now;

            await _repository.UpdateAsync(product);
            await _unitOfWork.SaveChangesAsync();

            var dto = _mapper.Map<ProductDTO>(product);
            _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", dto));
        }
    }
}