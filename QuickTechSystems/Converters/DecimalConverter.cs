using System;
using System.Globalization;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class DecimalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal decimalValue)
            {
                // Show 0 as "0" but any other value exactly as it is
                if (decimalValue == 0)
                    return "0";

                // Return the value exactly as it is - no formatting
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue)
            {
                // Handle empty or whitespace
                if (string.IsNullOrWhiteSpace(stringValue))
                    return 0m;

                // Clean the string and try to parse
                stringValue = stringValue.Replace(",", ".").Trim();

                // Try parsing
                if (decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
                    return result;

                return 0m;
            }
            return 0m;
        }
    }
}