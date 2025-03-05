using System;
using System.Globalization;
using System.Windows.Data;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Converters
{
    public class LowStockConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ProductDTO product)
            {
                // Show warning when stock is below minimum but above zero
                return product.CurrentStock < product.MinimumStock && product.CurrentStock > 0;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}