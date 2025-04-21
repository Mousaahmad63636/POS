using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.Services;

namespace QuickTechSystems.WPF.Converters
{
    public class FilePathToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // Skip if null or empty
                if (value == null || string.IsNullOrEmpty(value.ToString()))
                    return DependencyProperty.UnsetValue;

                string? imagePath = value.ToString();

                // Try to get the image service
                IImagePathService imageService = null;
                try
                {
                    imageService = ((App)System.Windows.Application.Current).ServiceProvider.GetService(typeof(IImagePathService)) as IImagePathService;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error resolving IImagePathService: {ex.Message}");
                }

                string fullPath;

                if (imageService != null)
                {
                    fullPath = imageService.GetFullImagePath(imagePath);
                }
                else
                {
                    // Fallback if service not available
                    if (Path.IsPathRooted(imagePath))
                    {
                        fullPath = imagePath;
                    }
                    else
                    {
                        fullPath = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "ProductImages",
                            imagePath
                        );
                    }
                }

                if (!File.Exists(fullPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Image file not found: {fullPath}");
                    return DependencyProperty.UnsetValue;
                }

                // Create a BitmapImage using a proper file URI
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;

                // MODIFIED LINE: Use the same file URI format as in ProductViewModel
                Uri uri = new Uri("file:///" + fullPath.Replace('\\', '/'));
                image.UriSource = uri;

                // If a size parameter is provided, resize the image
                if (parameter != null && int.TryParse(parameter.ToString(), out int size))
                {
                    image.DecodePixelWidth = size;
                    image.DecodePixelHeight = size;
                }

                image.EndInit();
                image.Freeze(); // Important for cross-thread usage

                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting file path to image: {ex.Message}");
                // Add fallback logic like in ProductViewModel if needed
                try
                {
                    // Get the path again since we lost scope
                    string? imagePath = value.ToString();
                    string retryPath;

                    IImagePathService imageService = ((App)System.Windows.Application.Current).ServiceProvider.GetService(typeof(IImagePathService)) as IImagePathService;

                    if (imageService != null)
                    {
                        retryPath = imageService.GetFullImagePath(imagePath);
                    }
                    else if (Path.IsPathRooted(imagePath))
                    {
                        retryPath = imagePath;
                    }
                    else
                    {
                        retryPath = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "ProductImages",
                            imagePath
                        );
                    }

                    // Fallback to stream-based loading
                    BitmapImage fallbackImage = new BitmapImage();

                    using (var stream = new FileStream(retryPath, FileMode.Open, FileAccess.Read))
                    {
                        fallbackImage.BeginInit();
                        fallbackImage.CacheOption = BitmapCacheOption.OnLoad;
                        fallbackImage.StreamSource = stream;
                        fallbackImage.EndInit();
                        fallbackImage.Freeze();
                    }

                    return fallbackImage;
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Fallback image loading also failed: {fallbackEx.Message}");
                    return DependencyProperty.UnsetValue;
                }
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}