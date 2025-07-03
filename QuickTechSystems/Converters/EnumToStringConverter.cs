using System;
using System.Globalization;
using System.Windows.Data;
using DomainEnums = QuickTechSystems.Domain.Enums;

namespace QuickTechSystems.WPF.Converters
{
    public class EnumToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DomainEnums.SortOption sortOption)
            {
                return sortOption switch
                {
                    DomainEnums.SortOption.Name => "Name",
                    DomainEnums.SortOption.PurchasePrice => "Purchase Price",
                    DomainEnums.SortOption.SalePrice => "Sale Price",
                    DomainEnums.SortOption.StockLevel => "Stock Level",
                    DomainEnums.SortOption.CreationDate => "Creation Date",
                    DomainEnums.SortOption.ProfitMargin => "Profit Margin",
                    DomainEnums.SortOption.TotalValue => "Total Value",
                    DomainEnums.SortOption.LastUpdated => "Last Updated",
                    _ => "Name"
                };
            }

            if (value is DomainEnums.StockStatus stockStatus)
            {
                return stockStatus switch
                {
                    DomainEnums.StockStatus.All => "All",
                    DomainEnums.StockStatus.OutOfStock => "Out of Stock",
                    DomainEnums.StockStatus.LowStock => "Low Stock",
                    DomainEnums.StockStatus.AdequateStock => "Adequate Stock",
                    DomainEnums.StockStatus.Overstocked => "Overstocked",
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