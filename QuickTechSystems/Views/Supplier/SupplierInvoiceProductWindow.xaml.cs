// File: QuickTechSystems\Views\Supplier\SupplierInvoiceProductWindow.xaml.cs
using System;
using System.Linq;
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
            QuickBarcodeTextBox?.Focus();
        }

        private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            try
            {
                var dataGrid = sender as DataGrid;
                var detail = e.Row.Item as SupplierInvoiceDetailDTO;

                if (detail != null && dataGrid != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[DataGrid] Beginning edit on {detail.ProductName}, Column: {e.Column.Header}");
                    e.Cancel = false;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[DataGrid] BeginningEdit: Could not get detail or dataGrid");
                    e.Cancel = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataGrid] Error in BeginningEdit: {ex.Message}");
                e.Cancel = true;
            }
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            try
            {
                var dataGrid = sender as DataGrid;
                var detail = e.Row.Item as SupplierInvoiceDetailDTO;

                if (dataGrid != null && detail != null && e.EditingElement is TextBox textBox)
                {
                    System.Diagnostics.Debug.WriteLine($"[DataGrid] Cell edit ending: {e.Column.Header}");
                    System.Diagnostics.Debug.WriteLine($"[DataGrid] Product: {detail.ProductName}");
                    System.Diagnostics.Debug.WriteLine($"[DataGrid] New value: '{textBox.Text}'");

                    if (e.EditAction == DataGridEditAction.Commit)
                    {
                        // Force the binding to update immediately
                        var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                        binding?.UpdateSource();
                        System.Diagnostics.Debug.WriteLine($"[DataGrid] Forced binding update for {e.Column.Header}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataGrid] Error in CellEditEnding: {ex.Message}");
            }
        }

        private void DataGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            try
            {
                var dataGrid = sender as DataGrid;
                if (dataGrid != null)
                {
                    // Commit any pending edits when cell changes
                    dataGrid.CommitEdit(DataGridEditingUnit.Row, true);

                    if (dataGrid.CurrentCell.Item is SupplierInvoiceDetailDTO detail && dataGrid.CurrentCell.Column != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DataGrid] Current cell changed to: {detail.ProductName}, Column: {dataGrid.CurrentCell.Column.Header}");
                    }

                    if (_viewModel != null && _viewModel.HasChanges)
                    {
                        System.Diagnostics.Debug.WriteLine("[DataGrid] ViewModel shows HasChanges = true");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataGrid] Error in CurrentCellChanged: {ex.Message}");
            }
        }

        private void DataGrid_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataGrid = sender as DataGrid;
                if (dataGrid != null)
                {
                    // Commit any pending edits when DataGrid loses focus
                    dataGrid.CommitEdit(DataGridEditingUnit.Row, true);
                    System.Diagnostics.Debug.WriteLine("[DataGrid] Lost focus, committed edits");

                    if (_viewModel != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DataGrid] After focus lost - HasChanges: {_viewModel.HasChanges}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataGrid] Error in LostFocus: {ex.Message}");
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
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
                    ProductNameSearchTextBox?.Focus();
                    e.Handled = true;
                    break;

                case Key.F4:
                    QuickBarcodeTextBox?.Focus();
                    e.Handled = true;
                    break;

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
                    // Remove Task.Run - execute directly on UI thread
                    _viewModel.BarcodeChangedCommand.Execute(textBox.Text);
                }
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

        private async void QuickBarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Trigger barcode processing immediately on Enter
                var textBox = sender as TextBox;
                if (textBox != null && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    if (_viewModel.BarcodeChangedCommand.CanExecute(textBox.Text))
                    {
                        // Remove Task.Run - execute directly on UI thread
                        _viewModel.BarcodeChangedCommand.Execute(textBox.Text);
                    }
                }
                e.Handled = true;
            }
        }

        public bool ResultSaved => _resultSaved;

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from events
            if (_viewModel != null)
            {
                _viewModel.RequestWindowClose -= () => { };
                _viewModel.Dispose();
            }
            base.OnClosed(e);
        }
    }
}