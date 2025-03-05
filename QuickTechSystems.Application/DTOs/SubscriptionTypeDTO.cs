using System;

namespace QuickTechSystems.Application.DTOs
{
    public class SubscriptionTypeDTO : BaseDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal AdditionalCharge { get; set; }
        public int CustomerCount { get; set; } // To track how many customers are using this type
    }
}