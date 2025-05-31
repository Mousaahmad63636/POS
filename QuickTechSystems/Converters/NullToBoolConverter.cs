// Path: QuickTechSystems.WPF.Converters/NullToBoolConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class NullToBoolConverter : IValueConverter
    {
        public static readonly NullToBoolConverter Instance = new NullToBoolConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Returns true if value is not null, false if it is null
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}