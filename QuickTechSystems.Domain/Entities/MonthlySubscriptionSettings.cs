using System;

namespace QuickTechSystems.Domain.Entities
{
    public class MonthlySubscriptionSettings
    {
        public int Id { get; set; }
        public decimal DefaultUnitPrice { get; set; }
        public DateTime LastModified { get; set; }
        public string ModifiedBy { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string Currency { get; set; } = "USD";
    }
}