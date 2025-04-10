using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.WPF.Services;

namespace QuickTechSystems.WPF.Views.Transaction.Components
{
    public partial class ProductSummaryTable : UserControl
    {
        public ProductSummaryTable()
        {
            InitializeComponent();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void Quantity_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is TransactionDetailDTO detail)
            {
                ShowQuantityKeypad(detail);
                e.Handled = true;
            }
        }

        private void PriceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is TransactionDetailDTO detail)
            {
                ShowPriceKeypad(detail);
            }
        }

        private void ShowPriceKeypad(TransactionDetailDTO detail)
        {
            if (InputDialogService.TryGetDecimalInput("Change Price", detail.ProductName, detail.UnitPrice, out decimal newPrice, Window.GetWindow(this)))
            {
                // Only allow price increases
                if (newPrice < detail.UnitPrice)
                {
                    MessageBox.Show(
                        "New price cannot be lower than current price.",
                        "Invalid Price",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Update price in data model
                detail.UnitPrice = newPrice;
                detail.Total = detail.Quantity * newPrice;

                // Update totals in view model
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.UpdateTotals();
            }
        }

        private void ShowQuantityKeypad(TransactionDetailDTO detail)
        {
            if (InputDialogService.TryGetDecimalInput("Change Quantity", detail.ProductName, detail.Quantity, out decimal newQuantity, Window.GetWindow(this)))
            {
                // Update quantity in data model
                detail.Quantity = newQuantity;
                detail.Total = detail.UnitPrice * newQuantity;

                // Update totals in view model
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.UpdateTotals();
            }
        }
    }
}