// Path: QuickTechSystems.WPF.Views/BulkMainStockDialog.xaml.cs
using System;
using System.ComponentModel;
using System.Threading.Tasks;
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
        public BulkMainStockDialog()
        {
            InitializeComponent();
            Loaded += BulkMainStockDialog_Loaded;
        }

        private void BulkMainStockDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure window is maximized on load
            this.WindowState = WindowState.Maximized;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Check if we're saving
            if (DataContext is BulkMainStockViewModel viewModel && viewModel.IsSaving)
            {
                var result = MessageBox.Show("A save operation is in progress. Are you sure you want to cancel?",
                    "Cancel Operation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
            }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // Set up a binding to automatically close the dialog when the ViewModel sets DialogResult
            if (DataContext is BulkMainStockViewModel viewModel)
            {
                viewModel.PropertyChanged += async (s, args) =>
                {
                    if (args.PropertyName == nameof(BulkMainStockViewModel.DialogResult) &&
                        viewModel.DialogResult.HasValue)
                    {
                        // Store the result value
                        bool result = viewModel.DialogResult.Value;

                        // If it's a successful save, give a little time for events to propagate
                        if (result)
                        {
                            await Task.Delay(300);
                        }

                        // Now close the dialog
                        this.DialogResult = result;
                    }
                };
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Check if we need to ensure the result is properly set
            if (DataContext is BulkMainStockViewModel viewModel &&
                !this.DialogResult.HasValue &&
                viewModel.DialogResultBackup.HasValue)
            {
                this.DialogResult = viewModel.DialogResultBackup.Value;
            }
        }

        private void BarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (sender is TextBox textBox && textBox.DataContext is MainStockDTO item &&
                    DataContext is BulkMainStockViewModel viewModel)
                {
                    viewModel.LookupProductCommand.Execute(item);

                    // Add barcode validation after lookup completes
                    ValidateBarcodePair(item);

                    e.Handled = true;
                }
            }
        }

        private void BoxBarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (sender is TextBox textBox && textBox.DataContext is MainStockDTO item &&
                    DataContext is BulkMainStockViewModel viewModel)
                {
                    viewModel.LookupBoxBarcodeCommand.Execute(item);

                    // Add barcode validation after lookup completes
                    ValidateBarcodePair(item);

                    e.Handled = true;
                }
            }
        }

        // Validate barcode pair
        private void ValidateBarcodePair(MainStockDTO item)
        {
            if (item == null) return;

            // Only validate when both fields have values
            if (!string.IsNullOrWhiteSpace(item.Barcode) && !string.IsNullOrWhiteSpace(item.BoxBarcode))
            {
                // If box barcode equals item barcode, automatically prefix it with "BX"
                if (item.BoxBarcode == item.Barcode)
                {
                    item.BoxBarcode = $"BX{item.Barcode}";
                }
            }
        }

        private void Barcode_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is MainStockDTO item)
            {
                // Validate barcodes when focus leaves either field
                ValidateBarcodePair(item);
            }
        }

        // Handle image preview on mouse down
        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Image image && image.Source is BitmapImage bitmapImage)
            {
                try
                {
                    // Create a new instance of BitmapImage for the preview (to avoid sharing the same instance)
                    BitmapImage previewImage = new BitmapImage();
                    previewImage.BeginInit();
                    previewImage.UriSource = bitmapImage.UriSource;
                    previewImage.CacheOption = BitmapCacheOption.OnLoad;
                    previewImage.EndInit();

                    // Create and show a simple image preview window
                    Window previewWindow = new Window
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
    }
}