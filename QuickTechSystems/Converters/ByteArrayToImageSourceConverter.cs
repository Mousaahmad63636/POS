using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace QuickTechSystems.WPF.Converters
{
    public class ByteArrayToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || !(value is byte[] imageData) || imageData.Length == 0)
                return null;

            try
            {
                var image = new BitmapImage();
                using (var ms = new MemoryStream(imageData))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.DecodePixelWidth = parameter is int width && width > 0 ? width : 0;
                    image.EndInit();
                    image.Freeze(); // Important for cross-thread usage
                }
                return image;
            }
            catch (Exception)
            {
                // Return null instead of throwing an exception
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}