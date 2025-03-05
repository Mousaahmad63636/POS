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
                if (parameter?.ToString() == "LBP")
                {
                    return CurrencyHelper.FormatLBP(CurrencyHelper.ConvertToLBP(amount));
                }
                return amount.ToString("C2");
            }
            return "0.00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}