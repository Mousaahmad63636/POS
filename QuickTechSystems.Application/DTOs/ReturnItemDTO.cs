using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickTechSystems.Application.DTOs
{
    public class ReturnItemDTO
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int OriginalQuantity { get; set; }
        public int QuantityToReturn { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal RefundAmount => QuantityToReturn * UnitPrice;
        public string? ReturnReason { get; set; }
    }
}
