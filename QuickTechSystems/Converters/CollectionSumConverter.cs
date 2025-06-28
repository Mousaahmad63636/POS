using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Reflection;

namespace QuickTechSystems.WPF.Converters
{
    public class CollectionSumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable collection && parameter is string propertyName)
            {
                try
                {
                    decimal sum = 0;

                    foreach (var item in collection)
                    {
                        if (item != null)
                        {
                            var property = item.GetType().GetProperty(propertyName);
                            if (property != null)
                            {
                                var propertyValue = property.GetValue(item);
                                if (propertyValue != null)
                                {
                                    // Try to convert to decimal
                                    if (decimal.TryParse(propertyValue.ToString(), out decimal decimalValue))
                                    {
                                        sum += decimalValue;
                                    }
                                }
                            }
                        }
                    }

                    return sum;
                }
                catch (Exception)
                {
                    return 0m;
                }
            }

            return 0m;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}