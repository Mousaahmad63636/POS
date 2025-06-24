using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.WPF.Converters
{
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TransactionStatus status)
            {
                return status switch
                {
                    TransactionStatus.Completed => Colors.Green,
                    TransactionStatus.Pending => Colors.Orange,
                    TransactionStatus.Cancelled => Colors.Red,
                    _ => Colors.Gray
                };
            }
            return Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}