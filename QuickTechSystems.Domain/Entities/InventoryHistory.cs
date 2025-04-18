// Path: QuickTechSystem.Domain/Entities/InventoryHistory.cs
using QuickTechSystems.Domain.Enums;

namespace QuickTechSystems.Domain.Entities
{
    public class InventoryHistory
    {
        public int InventoryHistoryId { get; set; }
        public int ProductId { get; set; }
        public int QuantityChanged { get; set; }
        public TransactionType OperationType { get; set; }
        public string? Reference { get; set; }
        public DateTime Date { get; set; }
        public string? Notes { get; set; }

        // Navigation property
        public virtual Product? Product { get; set; }
    }
}