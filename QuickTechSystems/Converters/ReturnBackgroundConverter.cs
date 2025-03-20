using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace QuickTechSystems.WPF.Converters
{
    /// <summary>
    /// Sets background color based on transaction return status
    /// </summary>
    public class ReturnBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return new SolidColorBrush(Colors.Transparent);

            string status = value.ToString();
            if (status.IndexOf("Return", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBE6"));
            }

            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}