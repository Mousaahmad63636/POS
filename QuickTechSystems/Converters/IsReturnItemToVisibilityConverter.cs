using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Converters
{
    /// <summary>
    /// Directly converts a transaction detail item to Visibility based on whether it's a return item
    /// </summary>
    public class IsReturnItemToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TransactionDetailDTO detail)
            {
                // Show the "RETURN" label if this is a return item
                if (detail.Quantity < 0 || detail.Total < 0)
                {
                    return Visibility.Visible;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}