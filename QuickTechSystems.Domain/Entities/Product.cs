using QuickTechSystems.Domain.Entities;

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
    public int MinimumStock { get; set; }
    public int? SupplierId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string? Speed { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? ImagePath { get; set; }

    public int? PlantsHardscapeId { get; set; }
    public int? LocalImportedId { get; set; }
    public int? IndoorOutdoorId { get; set; }
    public int? PlantFamilyId { get; set; }
    public int? DetailId { get; set; }

    public virtual Category? Category { get; set; }
    public virtual ICollection<TransactionDetail> TransactionDetails { get; set; } = new List<TransactionDetail>();
    public virtual ICollection<InventoryHistory> InventoryHistories { get; set; } = new List<InventoryHistory>();
    public virtual Supplier? Supplier { get; set; }
    public virtual Category? PlantsHardscape { get; set; }
    public virtual Category? LocalImported { get; set; }
    public virtual Category? IndoorOutdoor { get; set; }
    public virtual Category? PlantFamily { get; set; }
    public virtual Category? Detail { get; set; }
}