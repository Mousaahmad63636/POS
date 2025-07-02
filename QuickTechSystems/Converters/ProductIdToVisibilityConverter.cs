using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class ProductIdToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int productId)
            {
                // Show if ProductId is greater than 0 (existing product selected)
                return productId > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            // Hide if value is null or not an integer
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}