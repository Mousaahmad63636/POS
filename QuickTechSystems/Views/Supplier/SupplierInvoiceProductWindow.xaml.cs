// File: QuickTechSystems\Views\Supplier\SupplierInvoiceProductWindow.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.ViewModels.Supplier;

namespace QuickTechSystems.WPF.Views
{
    public partial class SupplierInvoiceProductWindow : Window
    {
        private readonly SupplierInvoiceProductViewModel _viewModel;
        private bool _resultSaved = false;

        public SupplierInvoiceProductWindow(SupplierInvoiceProductViewModel viewModel, SupplierInvoiceDTO invoice)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = _viewModel;

            if (invoice != null)
            {
                _viewModel.InitializeAsync(invoice).ConfigureAwait(false);
                Title = $"Add/Edit Products - Invoice #{invoice.InvoiceNumber}";
            }
            else
            {
                Title = "Add/Edit Products - New Invoice";
            }

            Loaded += SupplierInvoiceProductWindow_Loaded;
        }

        private void SupplierInvoiceProductWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Set focus to barcode textbox for immediate scanning
            if (QuickBarcodeTextBox != null)
            {
                QuickBarcodeTextBox.Focus();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    // Hide search results on Escape
                    if (_viewModel.ShowSearchResults)
                    {
                        _viewModel.ClearSearchCommand?.Execute(null);
                        e.Handled = true;
                    }
                    else
                    {
                        Close();
                    }
                    break;

                case Key.F5:
                    if (_viewModel.LoadDataCommand.CanExecute(null))
                    {
                        _viewModel.LoadDataCommand.Execute(null);
                    }
                    e.Handled = true;
                    break;

                case Key.S when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    if (_viewModel.SaveChangesCommand.CanExecute(null))
                    {
                        _viewModel.SaveChangesCommand.Execute(null);
                        _resultSaved = true;
                    }
                    e.Handled = true;
                    break;

                case Key.N when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    if (_viewModel.AddRowCommand.CanExecute(null))
                    {
                        _viewModel.AddRowCommand.Execute(null);
                    }
                    e.Handled = true;
                    break;

                case Key.F2:
                    if (_viewModel.OpenNewProductDialogCommand.CanExecute(null))
                    {
                        _viewModel.OpenNewProductDialogCommand.Execute(null);
                    }
                    e.Handled = true;
                    break;

                case Key.F3:
                    // Quick focus to product name search
                    ProductNameSearchTextBox?.Focus();
                    e.Handled = true;
                    break;

                case Key.F4:
                    // Quick focus to barcode search
                    QuickBarcodeTextBox?.Focus();
                    e.Handled = true;
                    break;

                case Key.Down:
                    // Navigate search results with arrow keys
                    if (_viewModel.ShowSearchResults && _viewModel.SearchResults?.Count > 0)
                    {
                        HandleSearchNavigation(true);
                        e.Handled = true;
                    }
                    break;

                case Key.Up:
                    // Navigate search results with arrow keys
                    if (_viewModel.ShowSearchResults && _viewModel.SearchResults?.Count > 0)
                    {
                        HandleSearchNavigation(false);
                        e.Handled = true;
                    }
                    break;

                case Key.Enter:
                    // Select highlighted search result
                    if (_viewModel.ShowSearchResults && _viewModel.SelectedSearchResult != null)
                    {
                        _viewModel.SelectSearchResultCommand?.Execute(_viewModel.SelectedSearchResult);
                        e.Handled = true;
                    }
                    // Or add product if form is filled
                    else if (_viewModel.NewProductRow?.ProductId > 0 &&
                             _viewModel.NewProductRow.Quantity > 0 &&
                             _viewModel.NewProductRow.PurchasePrice > 0)
                    {
                        _viewModel.AddRowCommand?.Execute(null);
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void HandleSearchNavigation(bool down)
        {
            if (_viewModel.SearchResults == null || _viewModel.SearchResults.Count == 0)
                return;

            int currentIndex = -1;
            if (_viewModel.SelectedSearchResult != null)
            {
                currentIndex = _viewModel.SearchResults.IndexOf(_viewModel.SelectedSearchResult);
            }

            if (down)
            {
                currentIndex = (currentIndex + 1) % _viewModel.SearchResults.Count;
            }
            else
            {
                currentIndex = currentIndex <= 0 ? _viewModel.SearchResults.Count - 1 : currentIndex - 1;
            }

            _viewModel.SelectedSearchResult = _viewModel.SearchResults[currentIndex];
        }

        private async void BarcodeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                if (_viewModel.BarcodeChangedCommand.CanExecute(textBox.Text))
                {
                    await System.Threading.Tasks.Task.Run(() =>
                        _viewModel.BarcodeChangedCommand.Execute(textBox.Text));
                }
            }
        }

        private void CalculationTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Update total price when purchase price or quantity changes
            if (_viewModel?.NewProductRow != null)
            {
                _viewModel.NewProductRow.TotalPrice = _viewModel.NewProductRow.Quantity * _viewModel.NewProductRow.PurchasePrice;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.HasChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save them before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        if (_viewModel.SaveChangesCommand.CanExecute(null))
                        {
                            _viewModel.SaveChangesCommand.Execute(null);
                            _resultSaved = true;
                        }
                        Close();
                        break;
                    case MessageBoxResult.No:
                        Close();
                        break;
                    case MessageBoxResult.Cancel:
                        break;
                }
            }
            else
            {
                Close();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel.HasChanges && !_resultSaved)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Are you sure you want to close without saving?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
            }
        }

        // Event handler for enhanced product name search functionality
        private void ProductNameSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (_viewModel.ShowSearchResults && _viewModel.SearchResults?.Count > 0)
                    {
                        HandleSearchNavigation(true);
                        e.Handled = true;
                    }
                    break;

                case Key.Up:
                    if (_viewModel.ShowSearchResults && _viewModel.SearchResults?.Count > 0)
                    {
                        HandleSearchNavigation(false);
                        e.Handled = true;
                    }
                    break;

                case Key.Enter:
                    if (_viewModel.ShowSearchResults && _viewModel.SelectedSearchResult != null)
                    {
                        _viewModel.SelectSearchResultCommand?.Execute(_viewModel.SelectedSearchResult);
                        e.Handled = true;
                    }
                    break;

                case Key.Escape:
                    _viewModel.ClearSearchCommand?.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        // Enhanced event handler for barcode scanning
        private void QuickBarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Trigger barcode processing immediately on Enter
                var textBox = sender as TextBox;
                if (textBox != null && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    BarcodeTextBox_LostFocus(sender, e);
                }
                e.Handled = true;
            }
        }

        public bool ResultSaved => _resultSaved;

        protected override void OnClosed(EventArgs e)
        {
            _viewModel?.Dispose();
            base.OnClosed(e);
        }
    }
}