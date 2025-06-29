using System.ComponentModel.DataAnnotations;

namespace QuickTechSystems.Application.DTOs
{
    public class NewProductFromInvoiceDTO
    {
        [Required(ErrorMessage = "Product name is required")]
        [StringLength(200, ErrorMessage = "Product name cannot exceed 200 characters")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Barcode is required")]
        public string Barcode { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required(ErrorMessage = "Category is required")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a valid category")]
        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Purchase price is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Purchase price must be greater than 0")]
        public decimal PurchasePrice { get; set; }

        [Required(ErrorMessage = "Sale price is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Sale price must be greater than 0")]
        public decimal SalePrice { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Current stock cannot be negative")]
        public decimal CurrentStock { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Storehouse quantity cannot be negative")]
        public decimal Storehouse { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Minimum stock cannot be negative")]
        public int MinimumStock { get; set; } = 0;

        public int SupplierId { get; set; }

        public string SupplierName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public string BoxBarcode { get; set; } = string.Empty;

        [Range(0, int.MaxValue, ErrorMessage = "Number of boxes cannot be negative")]
        public int NumberOfBoxes { get; set; } = 0;

        [Range(1, int.MaxValue, ErrorMessage = "Items per box must be at least 1")]
        public int ItemsPerBox { get; set; } = 1;

        [Range(0, double.MaxValue, ErrorMessage = "Box purchase price cannot be negative")]
        public decimal BoxPurchasePrice { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Box sale price cannot be negative")]
        public decimal BoxSalePrice { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Minimum box stock cannot be negative")]
        public int MinimumBoxStock { get; set; } = 0;

        [Range(0, double.MaxValue, ErrorMessage = "Wholesale price cannot be negative")]
        public decimal WholesalePrice { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Box wholesale price cannot be negative")]
        public decimal BoxWholesalePrice { get; set; }

        public string? ImagePath { get; set; }

        [Required(ErrorMessage = "Quantity is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Quantity must be greater than 0")]
        public decimal InvoiceQuantity { get; set; } = 1;

        public decimal InvoiceTotalPrice => InvoiceQuantity * PurchasePrice;

        public bool HasValidBarcode => !string.IsNullOrWhiteSpace(Barcode);
        public bool HasValidCategory => CategoryId > 0;
        public bool HasValidSupplier => SupplierId > 0;
        public bool IsBoxProduct => NumberOfBoxes > 0 && ItemsPerBox > 1;
    }
}