using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace QuickTechSystems.WPF.Converters
{
    /// <summary>
    /// Changes text color based on return status
    /// </summary>
    public class ReturnTextColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isReturn && isReturn)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CC0000"));
            }

            return System.Windows.Application.Current.Resources["PrimaryColor"] ??
                   new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0066CC"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}