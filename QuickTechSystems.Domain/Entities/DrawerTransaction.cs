using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickTechSystems.Domain.Entities
{
    public class DrawerTransaction
    {
        public int TransactionId { get; set; }
        public int DrawerId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = string.Empty;  // "CashIn" or "CashOut"
        public decimal Amount { get; set; }
        public decimal Balance { get; set; }
        public string? Notes { get; set; }

        // Navigation property
        public virtual Drawer? Drawer { get; set; }
    }
}