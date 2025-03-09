namespace QuickTechSystems.Domain.Entities
{
    public class TransactionDetail
    {
        public int TransactionDetailId { get; set; }
        public int TransactionId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }

        // New return-related properties
        public int ReturnedQuantity { get; set; }
        public bool IsReturned { get; set; }
        public DateTime? ReturnDate { get; set; }
        public string? ReturnReason { get; set; }

        // Navigation properties
        public virtual Transaction? Transaction { get; set; }
        public virtual Product? Product { get; set; }
    }
}