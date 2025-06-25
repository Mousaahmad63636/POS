namespace QuickTechSystems.Application.DTOs
{
    public class CustomerPaymentDTO : BaseDTO
    {
        public int PaymentId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int? DrawerTransactionId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string? Notes { get; set; }
        public string Status { get; set; } = "Completed";
        public string CreatedBy { get; set; } = string.Empty;
    }
}