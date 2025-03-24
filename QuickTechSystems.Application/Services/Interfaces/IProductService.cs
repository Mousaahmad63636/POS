// QuickTechSystems.Application/Services/Interfaces/IProductService.cs
using QuickTechSystems.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface IProductService : IBaseService<ProductDTO>
    {
        Task<IEnumerable<ProductDTO>> GetByCategoryAsync(int categoryId);
        Task<bool> UpdateStockAsync(int productId, int quantity);
        Task<IEnumerable<ProductDTO>> GetLowStockProductsAsync();
        Task<ProductDTO?> GetByBarcodeAsync(string barcode);
        Task<ProductDTO> FindProductByBarcodeAsync(string barcode, int excludeProductId = 0);

        // New method for batch processing
        Task<List<ProductDTO>> CreateBatchAsync(List<ProductDTO> products, IProgress<string>? progress = null);
    }
}