namespace QuickTechSystems.Application.DTOs
{
    public class SubscriptionPaymentDTO
    {
        public int Id { get; set; }
        public int CustomerSubscriptionId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string? Notes { get; set; }

        public bool IsValid(out string errorMessage)
        {
            if (Amount <= 0)
            {
                errorMessage = "Payment amount must be greater than zero.";
                return false;
            }

            if (CustomerSubscriptionId <= 0)
            {
                errorMessage = "Invalid customer subscription.";
                return false;
            }

            if (PaymentDate > DateTime.Now)
            {
                errorMessage = "Payment date cannot be in the future.";
                return false;
            }

            if (PaymentDate == DateTime.MinValue)
            {
                errorMessage = "Please specify a valid payment date.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(CustomerName))
            {
                errorMessage = "Customer name is required.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}