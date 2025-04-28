// Path: QuickTechSystems.Application.DTOs/SupplierInvoiceDetailDTO.cs
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
    }
}