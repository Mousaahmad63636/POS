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
                    Close();
                    break;
                case Key.F5:
                    if (_viewModel.LoadDataCommand.CanExecute(null))
                    {
                        _viewModel.LoadDataCommand.Execute(null);
                    }
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
            if (_viewModel?.NewProductRow != null)
            {
                _viewModel.NewProductRow.TotalPrice = _viewModel.NewProductRow.Quantity * _viewModel.NewProductRow.PurchasePrice;
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

        public bool ResultSaved => _resultSaved;

        protected override void OnClosed(EventArgs e)
        {
            _viewModel?.Dispose();
            base.OnClosed(e);
        }
    }
}