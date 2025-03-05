using System;
using System.Collections.Generic;

namespace QuickTechSystems.Domain.Entities
{
    public class CustomerSubscription
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public decimal OldCounter { get; set; }
        public decimal NewCounter { get; set; }
        public decimal BillAmount { get; set; }
        public DateTime LastBillDate { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public decimal PricePerUnit { get; set; } = 1;
        public decimal AdditionalFees { get; set; }
        public int? SubscriptionTypeId { get; set; }
        public virtual SubscriptionType? SubscriptionType { get; set; }

        // Navigation properties
        public virtual ICollection<SubscriptionPayment> Payments { get; set; } = new List<SubscriptionPayment>();
        public virtual ICollection<CounterHistory> CounterHistories { get; set; } = new List<CounterHistory>();
    }
}