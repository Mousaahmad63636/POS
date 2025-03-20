using System;
using System.Globalization;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    /// <summary>
    /// Converter that formats transaction status to highlight returned items
    /// </summary>
    public class ReturnStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "Unknown";

            string status = value.ToString();
            if (status.IndexOf("Return", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"RETURNED - {status}";
            }

            return status;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}