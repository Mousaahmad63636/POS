using System;
using System.Globalization;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class CardSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percentage && parameter is string dimension)
            {
                // Base sizes: 150x180 at 100%, 40x50 at 0%
                switch (dimension.ToLower())
                {
                    case "width":
                        return 40 + (percentage / 100.0) * (150 - 40); // 40 to 150
                    case "height":
                        return 50 + (percentage / 100.0) * (180 - 50); // 50 to 180
                    case "overlayheight":
                        var cardHeight = 50 + (percentage / 100.0) * (180 - 50);
                        return Math.Max(20, cardHeight * 0.5); // Half the card height, minimum 20
                    case "fontsize":
                        return 6 + (percentage / 100.0) * (13 - 6); // 6 to 13
                    case "pricefontsize":
                        return 7 + (percentage / 100.0) * (14 - 7); // 7 to 14
                    case "margin":
                        return 2 + (percentage / 100.0) * (8 - 2); // 2 to 8
                    case "padding":
                        return 2 + (percentage / 100.0) * (12 - 2); // 2 to 12
                    case "cornerradius":
                        return 3 + (percentage / 100.0) * (8 - 3); // 3 to 8
                    case "showoverlay":
                        return percentage >= 30; // Show overlay only when cards are 30% or larger
                    case "showprice":
                        return percentage >= 20; // Show price when cards are 20% or larger
                    case "showlbpprice":
                        return percentage >= 60; // Show LBP price only when cards are 60% or larger
                }
            }
            return 150; // Default width
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}