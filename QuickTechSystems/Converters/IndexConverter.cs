using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace QuickTechSystems.WPF.Converters
{
    public class IndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ContentPresenter contentPresenter)
            {
                try
                {
                    // Find the parent ItemsControl
                    var itemsControl = FindParent<ItemsControl>(contentPresenter);
                    if (itemsControl == null) return 1;

                    // Get the data context (the actual item)
                    var dataContext = contentPresenter.DataContext;
                    if (dataContext == null) return 1;

                    // Find the index of the item in the ItemsControl's Items collection
                    var index = itemsControl.Items.IndexOf(dataContext);

                    // Return 1-based index
                    return index >= 0 ? index + 1 : 1;
                }
                catch
                {
                    return 1;
                }
            }
            return 1;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            if (child == null) return null;

            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            if (parentObject is T parent)
            {
                return parent;
            }
            else
            {
                return FindParent<T>(parentObject);
            }
        }
    }
}