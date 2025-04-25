using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace QuickTechSystems.Application.Helpers
{
    public static class CurrencyHelper
    {
        private static decimal _exchangeRate = 100000m;
        public static void UpdateExchangeRate(decimal rate)
        {
            _exchangeRate = rate;
        }
        public static decimal ConvertToLBP(decimal usdAmount)
        {
            return usdAmount * _exchangeRate;
        }
        public static decimal RoundLBP(decimal lbpAmount)
        {
            // Round using custom rounding rules
            const decimal roundingUnit = 10000m;
            // Integer division to get the number of complete 10,000s
            decimal baseTenThousands = Math.Floor(lbpAmount / roundingUnit);
            // Get the remainder in LBP
            decimal remainder = lbpAmount - (baseTenThousands * roundingUnit);

            // Apply custom rounding rules:
            // - If remainder is between 2000 and 7000 (inclusive), round to 5000
            // - If remainder is above 7000, round up to the next 10000
            // - If remainder is below 2000, round down to the previous 10000
            if (remainder >= 2000m && remainder <= 7000m)
            {
                return baseTenThousands * roundingUnit + 5000m;
            }
            else if (remainder > 7000m)
            {
                return (baseTenThousands + 1) * roundingUnit;
            }
            else // remainder < 2000m
            {
                return baseTenThousands * roundingUnit;
            }
        }
        public static string FormatLBP(decimal lbpAmount)
        {
            // Apply custom rounding
            decimal roundedAmount = RoundLBP(lbpAmount);
            return $"{roundedAmount:N0} LBP";
        }
        public static string FormatDualCurrency(decimal usdAmount)
        {
            decimal lbpAmount = ConvertToLBP(usdAmount);
            decimal roundedAmount = RoundLBP(lbpAmount);
            return $"${usdAmount:N2} / {roundedAmount:N0} LBP";
        }
    }
}