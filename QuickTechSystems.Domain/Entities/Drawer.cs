using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickTechSystems.Domain.Entities
{
    public class Drawer
    {
        public int DrawerId { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal CashIn { get; set; }
        public decimal CashOut { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string Status { get; set; } = "Open"; // Open, Closed
        public string? Notes { get; set; }
        public string CashierId { get; set; } = string.Empty;
        public string CashierName { get; set; } = string.Empty;
        public virtual ICollection<DrawerTransaction> Transactions { get; set; } = new List<DrawerTransaction>();
    }
}
