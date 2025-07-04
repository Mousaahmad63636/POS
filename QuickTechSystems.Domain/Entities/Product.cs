﻿using System;
using System.Collections.Generic;

namespace QuickTechSystems.Domain.Entities
{
    public class Product
    {
        public int ProductId { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int CategoryId { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal SalePrice { get; set; }
        public decimal CurrentStock { get; set; }
        public decimal Storehouse { get; set; }
        public int MinimumStock { get; set; }
        public int? SupplierId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? ImagePath { get; set; }
        public byte[]? BarcodeImage { get; set; }

        public string BoxBarcode { get; set; } = string.Empty;
        public int NumberOfBoxes { get; set; }
        public int ItemsPerBox { get; set; } = 1;
        public decimal BoxPurchasePrice { get; set; }
        public decimal BoxSalePrice { get; set; }
        public int MinimumBoxStock { get; set; }

        public decimal WholesalePrice { get; set; }
        public decimal BoxWholesalePrice { get; set; }

        public virtual Category Category { get; set; } = null!;
        public virtual Supplier? Supplier { get; set; }
        public virtual ICollection<TransactionDetail> TransactionDetails { get; set; } = new List<TransactionDetail>();
        public virtual ICollection<InventoryHistory> InventoryHistories { get; set; } = new List<InventoryHistory>();
    }
}