// Path: QuickTechSystems.WPF.Converters/NullToBooleanConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class NullToBooleanConverter : IValueConverter
    {
        // Static instance for XAML usage
        public static readonly NullToBooleanConverter Instance = new NullToBooleanConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Handle byte arrays specifically (for barcode images)
            if (value is byte[] byteArray)
            {
                return byteArray != null && byteArray.Length > 0;
            }

            // Handle strings
            if (value is string stringValue)
            {
                return !string.IsNullOrWhiteSpace(stringValue);
            }

            // Default null check for other types
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}