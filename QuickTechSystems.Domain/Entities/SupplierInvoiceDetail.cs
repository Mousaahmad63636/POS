namespace QuickTechSystems.Domain.Entities
{
    public class SupplierInvoiceDetail
    {
        public int SupplierInvoiceDetailId { get; set; }
        public int SupplierInvoiceId { get; set; }
        public int ProductId { get; set; }
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

        public virtual SupplierInvoice SupplierInvoice { get; set; } = null!;
        public virtual Product Product { get; set; } = null!;
    }
}