using System;
using System.Globalization;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Views
{
    public class MonthConverter : IValueConverter
    {
        public static readonly MonthConverter Instance = new MonthConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int month && month >= 1 && month <= 12)
            {
                return new DateTime(2000, month, 1).ToString("MMMM", culture);
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}