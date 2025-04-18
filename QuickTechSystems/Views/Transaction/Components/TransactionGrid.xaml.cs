using System.Globalization;
using System.Windows;
using System.Windows.Controls;

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
    }
}