namespace QuickTechSystems.Application.DTOs
{
    public class ProductStatisticsDTO
    {
        public decimal TotalInventoryValue { get; set; }
        public decimal TotalRetailValue { get; set; }
        public decimal TotalPotentialProfit { get; set; }
        public int LowStockCount { get; set; }
        public int OutOfStockCount { get; set; }
        public int OverstockedCount { get; set; }
        public int TotalProducts { get; set; }
        public int ActiveProducts { get; set; }
        public int InactiveProducts { get; set; }
        public decimal AverageStockLevel { get; set; }
        public decimal AverageProfitMargin { get; set; }
    }
}