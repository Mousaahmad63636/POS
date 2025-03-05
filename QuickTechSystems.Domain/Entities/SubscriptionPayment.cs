using System;

namespace QuickTechSystems.Domain.Entities
{
    public class SubscriptionPayment
    {
        public int Id { get; set; }
        public int CustomerSubscriptionId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string? Notes { get; set; }

        // Navigation property
        public virtual CustomerSubscription CustomerSubscription { get; set; } = null!;
    }
}