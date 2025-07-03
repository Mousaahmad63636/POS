using QuickTechSystems.Domain.Enums;

namespace QuickTechSystems.Application.DTOs
{
    public class ProductFilterDTO
    {
        public string? SearchTerm { get; set; }
        public int? CategoryId { get; set; }
        public int? SupplierId { get; set; }
        public bool? IsActive { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public decimal? MinStock { get; set; }
        public decimal? MaxStock { get; set; }
        public StockStatus StockStatus { get; set; } = StockStatus.All;
        public SortOption SortBy { get; set; } = SortOption.Name;
        public bool SortDescending { get; set; } = false;
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }
}