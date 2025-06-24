namespace QuickTechSystems.Application.DTOs
{
    public class SupplierInvoiceDetailDTO
    {
        public int SupplierInvoiceDetailId { get; set; }
        public int SupplierInvoiceId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductBarcode { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal TotalPrice { get; set; }

        public string BoxBarcode { get; set; } = string.Empty;
        public int NumberOfBoxes { get; set; }
        public int ItemsPerBox { get; set; } = 1;
        public decimal BoxPurchasePrice { get; set; }
        public decimal BoxSalePrice { get; set; }

        public decimal CurrentStock { get; set; }
        public decimal Storehouse { get; set; }
        public decimal SalePrice { get; set; }
        public decimal WholesalePrice { get; set; }
        public decimal BoxWholesalePrice { get; set; }
        public int MinimumStock { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;

        public decimal AvailableBoxes => ItemsPerBox > 0 ? Math.Floor(Storehouse / ItemsPerBox) : 0;
        public decimal ItemPurchasePrice => ItemsPerBox > 0 && ItemsPerBox != 1 ? BoxPurchasePrice / ItemsPerBox : PurchasePrice;
        public decimal ItemWholesalePrice => ItemsPerBox > 0 && ItemsPerBox != 1 ? BoxWholesalePrice / ItemsPerBox : WholesalePrice;

        public string StockStatus => CurrentStock <= MinimumStock ? "Low Stock" : "Normal";
        public string StorehouseStatus => Storehouse <= 0 ? "Empty" : "Available";

        public decimal TotalInventory => CurrentStock + Storehouse;
        public decimal EquivalentBoxes => ItemsPerBox > 0 ? Math.Floor(TotalInventory / ItemsPerBox) : 0;
        public string QuantityBreakdown => $"Stock: {CurrentStock}, Warehouse: {Storehouse}";
    }
}