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

            // Initialize the ViewModel with the invoice
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
            // Focus handling or any initialization after window is loaded
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Handle keyboard shortcuts
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    break;
                case Key.F5:
                    // Refresh data
                    if (_viewModel.LoadDataCommand.CanExecute(null))
                    {
                        _viewModel.LoadDataCommand.Execute(null);
                    }
                    break;
                case Key.S when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    // Ctrl+S to save
                    if (_viewModel.SaveChangesCommand.CanExecute(null))
                    {
                        _viewModel.SaveChangesCommand.Execute(null);
                        _resultSaved = true;
                    }
                    e.Handled = true;
                    break;
                case Key.N when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    // Ctrl+N to add new product
                    if (_viewModel.AddRowCommand.CanExecute(null))
                    {
                        _viewModel.AddRowCommand.Execute(null);
                    }
                    e.Handled = true;
                    break;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if there are unsaved changes
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
                        break;
                    case MessageBoxResult.Cancel:
                        return; // Don't close
                    case MessageBoxResult.No:
                        break; // Close without saving
                }
            }

            DialogResult = _resultSaved;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Additional cleanup if needed
            base.OnClosing(e);
        }

        private void BarcodeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && DataContext is SupplierInvoiceProductViewModel viewModel)
            {
                if (viewModel.BarcodeChangedCommand?.CanExecute(textBox.Text) == true)
                {
                    viewModel.BarcodeChangedCommand.Execute(textBox.Text);
                }
            }
        }

        private void CalculationTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierInvoiceProductViewModel viewModel)
            {
                if (viewModel.UpdateNewProductCommand?.CanExecute(null) == true)
                {
                    viewModel.UpdateNewProductCommand.Execute(null);
                }
            }
        }
    }
}