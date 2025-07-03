using QuickTechSystems.Domain.Enums;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace QuickTechSystems.WPF.Converters
{
    public class StockStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StockStatus status)
            {
                return status switch
                {
                    StockStatus.OutOfStock => "Out of Stock",
                    StockStatus.LowStock => "Low Stock",
                    StockStatus.AdequateStock => "Adequate Stock",
                    StockStatus.Overstocked => "Overstocked",
                    _ => "All"
                };
            }
            return "All";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string statusString)
            {
                return statusString switch
                {
                    "Out of Stock" => StockStatus.OutOfStock,
                    "Low Stock" => StockStatus.LowStock,
                    "Adequate Stock" => StockStatus.AdequateStock,
                    "Overstocked" => StockStatus.Overstocked,
                    _ => StockStatus.All
                };
            }
            return StockStatus.All;
        }
    }

    public class StockStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StockStatus status)
            {
                return status switch
                {
                    StockStatus.OutOfStock => new SolidColorBrush(Colors.Red),
                    StockStatus.LowStock => new SolidColorBrush(Colors.Orange),
                    StockStatus.AdequateStock => new SolidColorBrush(Colors.Green),
                    StockStatus.Overstocked => new SolidColorBrush(Colors.Blue),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}