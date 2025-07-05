using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    /// <summary>
    /// Converter that converts a boolean value to a GridLength for dynamic column sizing
    /// </summary>
    public class BooleanToGridLengthConverter : IValueConverter
    {
        public GridLength TrueValue { get; set; } = new GridLength(400);
        public GridLength FalseValue { get; set; } = new GridLength(0);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueValue : FalseValue;
            }
            return FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is GridLength gridLength)
            {
                return gridLength.Value > 0;
            }
            return false;
        }
    }
}