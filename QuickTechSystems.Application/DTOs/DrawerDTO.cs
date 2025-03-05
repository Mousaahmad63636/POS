using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickTechSystems.Application.DTOs
{
    public class DrawerDTO
    {
        public int DrawerId { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal CashIn { get; set; }
        public decimal CashOut { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string Status { get; set; } = "Open";
        public string? Notes { get; set; }
        public string CashierId { get; set; } = string.Empty;
        public string CashierName { get; set; } = string.Empty;
        public decimal ExpectedBalance => OpeningBalance + CashIn - CashOut;
        public decimal Difference => CurrentBalance - ExpectedBalance;
    }
}
