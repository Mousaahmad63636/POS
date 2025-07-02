using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class NotEqualToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null && parameter == null)
                return Visibility.Collapsed;

            if (value == null)
                return Visibility.Visible;

            if (parameter == null)
                return Visibility.Visible;

            // Convert both values to strings for comparison
            string valueString = value.ToString();
            string parameterString = parameter.ToString();

            // Return Visible if they are NOT equal, Collapsed if they are equal
            return !string.Equals(valueString, parameterString, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}