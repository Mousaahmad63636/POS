using System;
using System.Globalization;
using System.Windows.Data;
using QuickTechSystems.Domain.Enums;

namespace QuickTechSystems.WPF.Converters
{
    public class EnumToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SortOption sortOption)
            {
                return sortOption switch
                {
                    SortOption.Name => "Name",
                    SortOption.PurchasePrice => "Purchase Price",
                    SortOption.SalePrice => "Sale Price",
                    SortOption.StockLevel => "Stock Level",
                    SortOption.CreationDate => "Creation Date",
                    SortOption.ProfitMargin => "Profit Margin",
                    SortOption.TotalValue => "Total Value",
                    SortOption.LastUpdated => "Last Updated",
                    _ => "Name"
                };
            }

            if (value is StockStatus stockStatus)
            {
                return stockStatus switch
                {
                    StockStatus.All => "All",
                    StockStatus.OutOfStock => "Out of Stock",
                    StockStatus.LowStock => "Low Stock",
                    StockStatus.AdequateStock => "Adequate Stock",
                    StockStatus.Overstocked => "Overstocked",
                    _ => "All"
                };
            }

            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}