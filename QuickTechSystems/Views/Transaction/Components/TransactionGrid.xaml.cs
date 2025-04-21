using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input; // Added for TextCompositionEventArgs
using QuickTechSystems.WPF.Services;
using QuickTechSystems.Application.DTOs; // Make sure this is included for TransactionDetailDTO

namespace QuickTechSystems.WPF.Views.Transaction.Components
{
    public partial class TransactionGrid : UserControl
    {
        public TransactionGrid()
        {
            InitializeComponent();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                foreach (TransactionDetailDTO item in dataGrid.Items)
                {
                    item.IsSelected = dataGrid.SelectedItems.Contains(item);
                }
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
                // Get the view model
                var viewModel = DataContext as TransactionViewModel;
                if (viewModel == null) return;

                // Get the cost price for the product
                decimal costPrice = viewModel.GetProductCostPrice(detail.ProductId);

                // Only allow prices equal to or higher than cost price
                if (newPrice < costPrice)
                {
                    MessageBox.Show(
                        "New price cannot be lower than cost price.",
                        "Invalid Price",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Update price in data model
                detail.UnitPrice = newPrice;
                detail.Total = detail.Quantity * newPrice;

                // Update totals in view model
                viewModel.UpdateTotals();
            }
        }
        private void UnitPrice_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is TransactionDetailDTO detail)
            {
                // Better decimal parsing with culture handling
                string textValue = textBox.Text.Replace("$", "").Replace(",", "").Trim();
                if (decimal.TryParse(textValue,
                                     NumberStyles.Number | NumberStyles.AllowDecimalPoint,
                                     CultureInfo.InvariantCulture,
                                     out decimal newPrice))
                {
                    // Valid price
                    if (newPrice < 0)
                    {
                        MessageBox.Show(
                            "Price cannot be negative. Value reset to original price.",
                            "Invalid Price",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        // Reset to original value
                        textBox.Text = detail.UnitPrice.ToString("C2");
                        return;
                    }

                    detail.UnitPrice = newPrice;
                    detail.Total = detail.Quantity * newPrice;

                    var viewModel = DataContext as TransactionViewModel;
                    viewModel?.UpdateTotals();
                }
                else
                {
                    // Invalid price
                    MessageBox.Show(
                        "Please enter a valid price. Value reset to original price.",
                        "Invalid Price",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    // Reset to original value
                    textBox.Text = detail.UnitPrice.ToString("C2");
                }
            }
        }

        // First missing method: Validates input characters for quantity field
        private void Quantity_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow digits and at most one decimal point
            if (!char.IsDigit(e.Text[0]) && e.Text[0] != '.')
            {
                e.Handled = true;
                return;
            }

            // Special handling for decimal point
            if (e.Text[0] == '.')
            {
                if (sender is TextBox textBox && textBox.Text.Contains("."))
                {
                    // Already has a decimal point, prevent adding another
                    e.Handled = true;
                    return;
                }
            }
        }

        // Second missing method: Validates quantity when focus leaves the field
        private void Quantity_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is TransactionDetailDTO detail)
            {
                // Parse the quantity
                if (decimal.TryParse(textBox.Text,
                                     NumberStyles.Number | NumberStyles.AllowDecimalPoint,
                                     CultureInfo.InvariantCulture,
                                     out decimal newQuantity))
                {
                    // Valid quantity
                    if (newQuantity <= 0)
                    {
                        MessageBox.Show(
                            "Quantity must be positive. Value reset to original quantity.",
                            "Invalid Quantity",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        // Reset to original value
                        textBox.Text = detail.Quantity.ToString();
                        return;
                    }

                    // Update quantity in data model
                    detail.Quantity = newQuantity;
                    detail.Total = detail.UnitPrice * newQuantity;

                    // Update totals in view model
                    var viewModel = DataContext as TransactionViewModel;
                    viewModel?.UpdateTotals();
                }
                else
                {
                    // Invalid quantity
                    MessageBox.Show(
                        "Please enter a valid quantity. Value reset to original quantity.",
                        "Invalid Quantity",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    // Reset to original value
                    textBox.Text = detail.Quantity.ToString();
                }
            }
        }
    }
}