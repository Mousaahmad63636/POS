namespace QuickTechSystems.Domain.Entities
{
    public class Customer
    {
        public int CustomerId { get; set; }
        public required string Name { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public decimal Balance { get; set; }
        public decimal CreditLimit { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
        public virtual ICollection<CustomerPayment> Payments { get; set; } = new List<CustomerPayment>();
    }

    public class CustomerPayment
    {
        public int CustomerPaymentId { get; set; }
        public int CustomerId { get; set; }
        public required decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public required string PaymentMethod { get; set; }
        public string Notes { get; set; } = string.Empty;
        public required virtual Customer Customer { get; set; }
    }
}