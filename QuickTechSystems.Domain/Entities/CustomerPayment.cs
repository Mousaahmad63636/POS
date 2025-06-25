namespace QuickTechSystems.Domain.Entities
{
    public class CustomerPayment
    {
        public int PaymentId { get; set; }
        public int CustomerId { get; set; }
        public int? DrawerTransactionId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string? Notes { get; set; }
        public string Status { get; set; } = "Completed"; // Pending, Completed, Cancelled
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;

        // Navigation properties
        public virtual Customer Customer { get; set; } = null!;
        public virtual DrawerTransaction? DrawerTransaction { get; set; }
    }
}