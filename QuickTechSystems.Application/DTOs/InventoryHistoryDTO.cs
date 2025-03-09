using QuickTechSystems.Domain.Enums;

namespace QuickTechSystems.Application.DTOs
{
    public class InventoryHistoryDTO
    {
        public DateTime Date { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int QuantityChanged { get; set; }
        public TransactionType OperationType { get; set; }
        public string Reference { get; set; } = string.Empty;
    }
}