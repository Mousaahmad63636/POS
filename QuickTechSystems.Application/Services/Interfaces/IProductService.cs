using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface IProductService : IBaseService<ProductDTO>
    {
        Task<IEnumerable<ProductDTO>> GetByCategoryAsync(int categoryId);
        Task<bool> UpdateStockAsync(int productId, int quantity);
        Task<IEnumerable<ProductDTO>> GetLowStockProductsAsync();
        Task<ProductDTO?> GetByBarcodeAsync(string barcode);
    }
}