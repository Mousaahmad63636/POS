using System;
using System.Globalization;
using System.Windows.Data;
using QuickTechSystems.Application.Helpers;

namespace QuickTechSystems.WPF.Converters
{
    public class UsdToLbpConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal usdAmount)
            {
                decimal lbpAmount = CurrencyHelper.RoundLBP(CurrencyHelper.ConvertToLBP(usdAmount));
                return $"{lbpAmount:N0} LBP";
            }

            return "0 LBP";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}