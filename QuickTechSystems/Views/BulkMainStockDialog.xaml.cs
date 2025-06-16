using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class BulkMainStockDialog : Window
    {
        private static readonly Regex _integerRegex = new Regex(@"^[0-9]*$");

        public BulkMainStockDialog()
        {
            InitializeComponent();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (sender is TextBox textBox &&
                    DataContext is BulkMainStockViewModel viewModel &&
                    textBox.DataContext is MainStockDTO item)
                {
                    viewModel.LookupProductCommand.Execute(item);
                    e.Handled = true;
                }
            }
        }

        private void BoxBarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (sender is TextBox textBox &&
                    DataContext is BulkMainStockViewModel viewModel &&
                    textBox.DataContext is MainStockDTO item)
                {
                    viewModel.LookupBoxBarcodeCommand.Execute(item);
                    e.Handled = true;
                }
            }
        }

        private void Barcode_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox &&
                DataContext is BulkMainStockViewModel viewModel &&
                textBox.DataContext is MainStockDTO item)
            {
                viewModel.ValidateItemCommand.Execute(item);
            }
        }

        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Image image && image.Source is BitmapImage bitmapImage)
            {
                try
                {
                    var previewImage = new BitmapImage();
                    previewImage.BeginInit();
                    previewImage.UriSource = bitmapImage.UriSource;
                    previewImage.CacheOption = BitmapCacheOption.OnLoad;
                    previewImage.EndInit();

                    var previewWindow = new Window
                    {
                        Title = "Image Preview",
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Width = 500,
                        Height = 500,
                        ResizeMode = ResizeMode.CanResize,
                        Content = new ScrollViewer
                        {
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            Content = new Image
                            {
                                Source = previewImage,
                                Stretch = System.Windows.Media.Stretch.None
                            }
                        }
                    };

                    previewWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error showing image preview: {ex.Message}",
                        "Preview Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DecimalTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow all digits and decimal point - NO RESTRICTIONS
            if (char.IsDigit(e.Text, 0) || e.Text == ".")
            {
                var textBox = sender as TextBox;

                // Only prevent multiple decimal points
                if (e.Text == "." && textBox.Text.Contains("."))
                {
                    e.Handled = true;
                }
                else
                {
                    e.Handled = false; // Allow everything else
                }
            }
            else
            {
                e.Handled = true; // Block non-numeric characters
            }
        }

        private void IntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow digits for integer fields
            e.Handled = !char.IsDigit(e.Text, 0);
        }

        private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                var textBox = sender as TextBox;
                var isInteger = (string)textBox.Tag == "integer";

                if (isInteger)
                {
                    // For integer fields, only allow digits
                    if (!_integerRegex.IsMatch(text))
                    {
                        e.CancelCommand();
                    }
                }
                else
                {
                    // For decimal fields, allow numbers and one decimal point
                    if (!IsValidDecimalText(text))
                    {
                        e.CancelCommand();
                    }
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private bool IsValidDecimalText(string text)
        {
            // Simple check: allow digits and at most one decimal point
            if (string.IsNullOrEmpty(text)) return true;

            int decimalCount = 0;
            foreach (char c in text)
            {
                if (c == '.')
                {
                    decimalCount++;
                    if (decimalCount > 1) return false;
                }
                else if (!char.IsDigit(c))
                {
                    return false;
                }
            }
            return true;
        }

        private void PriceTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Simply select all text - no clearing, no forcing
                textBox.SelectAll();
            }
        }

        private void PriceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // DO NOTHING - Let user input remain exactly as they typed it
            // No formatting, no forcing values, no changes
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (DataContext is BulkMainStockViewModel viewModel)
            {
                viewModel.Dispose();
            }
        }
    }
}