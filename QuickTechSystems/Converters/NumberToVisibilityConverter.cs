// Path: QuickTechSystems.WPF/Converters/NumberToVisibilityConverter.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class NumberToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Visibility.Collapsed;

            // Try to parse the value as a number
            int number;
            if (value is int intValue)
            {
                number = intValue;
            }
            else if (int.TryParse(value.ToString(), out int parsedValue))
            {
                number = parsedValue;
            }
            else
            {
                return Visibility.Collapsed;
            }

            // Check if we should show when zero
            bool showWhenZero = parameter?.ToString()?.Equals("zero", StringComparison.OrdinalIgnoreCase) == true;

            // Return visibility based on the number and parameter
            if (showWhenZero)
            {
                return number == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                return number > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}