using System;
using System.Globalization;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class SafeDecimalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal decimalValue)
            {
                return decimalValue.ToString("F2", culture);
            }
            return "0.00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue)
            {
                // Handle empty or whitespace
                if (string.IsNullOrWhiteSpace(stringValue))
                    return 0m;

                // Remove any invalid characters that might cause issues
                string cleanedValue = stringValue.Trim();

                // Handle special characters that might cause issues
                if (cleanedValue.Contains("`") || cleanedValue.Contains("~") || cleanedValue.Contains("@"))
                {
                    // Remove invalid characters
                    cleanedValue = System.Text.RegularExpressions.Regex.Replace(cleanedValue, @"[`~@#$%^&*()+=\[\]{}|\\:;""'<>?/]", "");
                }

                // Try parsing with the current culture
                if (decimal.TryParse(cleanedValue, NumberStyles.Any, culture, out decimal result))
                    return result;

                // Try parsing with invariant culture as fallback
                if (decimal.TryParse(cleanedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
                    return result;

                // If all parsing fails, return 0
                return 0m;
            }
            return 0m;
        }
    }
}