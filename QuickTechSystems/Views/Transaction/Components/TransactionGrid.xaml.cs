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
                if (decimal.TryParse(textBox.Text.Replace("$", "").Trim(), out decimal newPrice))
                {
                    detail.UnitPrice = newPrice;
                    detail.Total = detail.Quantity * newPrice;

                    var viewModel = DataContext as TransactionViewModel;
                    viewModel?.UpdateTotals();
                }
            }
        }
    }
}