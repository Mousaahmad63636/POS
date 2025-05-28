// Path: QuickTechSystem.Domain/Entities/InventoryHistory.cs
using QuickTechSystems.Domain.Enums;

namespace QuickTechSystems.Domain.Entities
{
    public class InventoryHistory
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public decimal QuantityChanged { get; set; } // Changed from int to decimal
        public TransactionType OperationType { get; set; }
        public DateTime Date { get; set; }
        public string Reference { get; set; }
        public string Notes { get; set; }

        // Navigation properties
        public virtual Product Product { get; set; }
    }
}