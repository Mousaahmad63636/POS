﻿// QuickTechSystems/Views/BulkProcessingStatusWindow.xaml.cs
using System;
using System.Windows;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Views
{
    /// <summary>
    /// Interaction logic for BulkProcessingStatusWindow.xaml
    /// </summary>
    public partial class BulkProcessingStatusWindow : Window
    {
        public BulkProcessingStatusWindow()
        {
            InitializeComponent();

            // Resource is now defined in XAML, so we don't need to add it here
        }
    }

    /// <summary>
    /// Converts a value to a Visibility based on a comparison with a parameter
    /// </summary>
    public class VisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
                return Visibility.Collapsed;

            double threshold = 0;

            if (parameter != null)
            {
                double.TryParse(parameter.ToString(), out threshold);
            }

            double doubleValue = System.Convert.ToDouble(value);

            return doubleValue > threshold ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}