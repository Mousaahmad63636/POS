using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Converters
{
    public class InvoiceDetailsTotalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable collection)
            {
                try
                {
                    decimal total = 0;

                    foreach (var item in collection)
                    {
                        if (item is SupplierInvoiceDetailDTO detail)
                        {
                            total += detail.TotalPrice;
                        }
                    }

                    // Return the decimal value so XAML StringFormat can handle formatting
                    return total;
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