// Create a new file called NullToVisibilityConverter.cs in the Converters folder
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter as string == "invert";
            bool isNull = value == null;

            if (invert)
                return isNull ? Visibility.Visible : Visibility.Collapsed;
            else
                return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}