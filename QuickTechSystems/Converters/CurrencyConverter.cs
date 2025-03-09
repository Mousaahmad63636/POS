using System;
using System.Globalization;
using System.Windows.Data;
using QuickTechSystems.Application.Helpers;

namespace QuickTechSystems.WPF.Converters
{
    public class CurrencyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal amount)
            {
                // If parameter is "LBP", convert USD to LBP
                if (parameter is string currencyCode && currencyCode == "LBP")
                {
                    // Convert USD to LBP using the helper
                    decimal lbpAmount = CurrencyHelper.ConvertToLBP(amount);
                    return CurrencyHelper.FormatLBP(lbpAmount);
                }
                // Default to USD
                return string.Format("{0:C2}", amount);
            }
            return "0.00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}