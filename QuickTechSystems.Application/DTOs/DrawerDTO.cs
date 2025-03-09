using System;

namespace QuickTechSystems.Application.DTOs
{
    public class DrawerDTO
    {
        public int DrawerId { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal CashIn { get; set; }
        public decimal CashOut { get; set; }
        public decimal TotalSales { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal TotalDebtPayments { get; set; }
        public decimal TotalSupplierPayments { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string Status { get; set; } = "Open";
        public string? Notes { get; set; }
        public string CashierId { get; set; } = string.Empty;
        public string CashierName { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }

        // Financial Calculations
        public decimal DailySales { get; set; }
        public decimal DailyReturns { get; set; }
        public decimal DailyExpenses { get; set; }
        public decimal DailyDebtPayments { get; set; }
        public decimal DailySupplierPayments { get; set; }  // Added missing property

        // Computed properties with explicit calculations
        public decimal NetSales => Math.Abs(TotalSales) - Math.Abs(TotalReturns);

        public decimal ExpectedBalance => OpeningBalance + CashIn - CashOut;

        public decimal Difference => CurrentBalance - ExpectedBalance;

        public decimal NetCashflow =>
            Math.Abs(TotalSales) +
            Math.Abs(TotalDebtPayments) -
            (Math.Abs(TotalExpenses) + Math.Abs(TotalSupplierPayments) + Math.Abs(TotalReturns));
    

        // Duration properties
        public TimeSpan Duration
        {
            get
            {
                if (Status.Equals("Closed", StringComparison.OrdinalIgnoreCase) && ClosedAt.HasValue)
                {
                    return ClosedAt.Value - OpenedAt;
                }
                return DateTime.Now - OpenedAt;
            }
        }

        // Validation properties
        public bool HasDiscrepancy => Math.Abs(Difference) > 0;
        public bool IsOverdrawn => CurrentBalance < 0;
        public bool IsNegativeNetCashflow => NetCashflow < 0;
        public bool IsClosed => Status.Equals("Closed", StringComparison.OrdinalIgnoreCase);

        // Helper methods
        public string GetFormattedDuration()
        {
            var duration = Duration;
            return duration.TotalHours >= 24
                ? $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m"
                : $"{duration.Hours}h {duration.Minutes}m";
        }

        public decimal GetNetAmount(string transactionType)
        {
            return transactionType.ToLower() switch
            {
                "sales" => NetSales,
                "expenses" => TotalExpenses + TotalSupplierPayments,
                "debt payments" => TotalDebtPayments,
                "returns" => TotalReturns,
                "supplier payments" => TotalSupplierPayments,
                "daily sales" => DailySales,
                "daily returns" => DailyReturns,
                "daily expenses" => DailyExpenses + DailySupplierPayments,
                "daily debt payments" => DailyDebtPayments,
                _ => 0
            };
        }

        public bool IsWithinOperatingHours()
        {
            var now = DateTime.Now.TimeOfDay;
            var start = TimeSpan.FromHours(6); // 6 AM
            var end = TimeSpan.FromHours(23); // 11 PM
            return now >= start && now <= end;
        }

        public void ResetDailyTotals()
        {
            DailySales = 0;
            DailyReturns = 0;
            DailyExpenses = 0;
            DailyDebtPayments = 0;
            DailySupplierPayments = 0;
        }
    }
}