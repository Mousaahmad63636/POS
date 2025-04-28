// Path: QuickTechSystems.Domain.Entities/SupplierInvoiceDetail.cs
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

        // Navigation properties
        public virtual SupplierInvoice? SupplierInvoice { get; set; }
        public virtual Product? Product { get; set; }
    }
}