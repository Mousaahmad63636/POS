using System;
using System.Globalization;
using System.Windows.Data;
using WpfEnums = QuickTechSystems.WPF.Enums;

namespace QuickTechSystems.WPF.Converters
{
    public class EnumToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is WpfEnums.SortOption sortOption)
            {
                return sortOption switch
                {
                    WpfEnums.SortOption.Name => "Name",
                    WpfEnums.SortOption.PurchasePrice => "Purchase Price",
                    WpfEnums.SortOption.SalePrice => "Sale Price",
                    WpfEnums.SortOption.StockLevel => "Stock Level",
                    WpfEnums.SortOption.CreationDate => "Creation Date",
                    WpfEnums.SortOption.ProfitMargin => "Profit Margin",
                    WpfEnums.SortOption.TotalValue => "Total Value",
                    WpfEnums.SortOption.LastUpdated => "Last Updated",
                    _ => "Name"
                };
            }

            if (value is WpfEnums.StockStatus stockStatus)
            {
                return stockStatus switch
                {
                    WpfEnums.StockStatus.All => "All",
                    WpfEnums.StockStatus.OutOfStock => "Out of Stock",
                    WpfEnums.StockStatus.LowStock => "Low Stock",
                    WpfEnums.StockStatus.AdequateStock => "Adequate Stock",
                    WpfEnums.StockStatus.Overstocked => "Overstocked",
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