using System.Windows.Controls;
using System.Windows.Input;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionView : UserControl
    {
        public TransactionView()
        {
            InitializeComponent();
            SetupKeyboardShortcuts();
            Loaded += TransactionView_Loaded;  // Add this line to subscribe to the Loaded event
        }

        private void BarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                var vm = DataContext as TransactionViewModel;
                vm?.ProcessBarcodeInput();
            }
        }
        private void ProductSearchGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ProductDTO selectedProduct)
            {
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.OnProductSelected(selectedProduct);
            }
        }
        private void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && item.DataContext is ProductDTO product)
            {
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.OnProductSelected(product);
            }
        }
        private void ListViewItem_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is ListViewItem item && item.DataContext is ProductDTO product)
            {
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.OnProductSelected(product);
                e.Handled = true;
            }
        }
        private void TransactionView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TransactionViewModel vm)
            {
                vm.OwnerWindow = Window.GetWindow(this);  // Set the hosting window
            }
        }
        private void SetupKeyboardShortcuts()
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.KeyDown += (s, e) =>
                {
                    var vm = DataContext as TransactionViewModel;
                    if (vm == null) return;

                    switch (e.Key)
                    {
                        case Key.F2: vm.VoidLastItemCommand.Execute(null); break;
                        case Key.F3: vm.ChangeQuantityCommand.Execute(null); break;
                        case Key.F4: vm.PriceCheckCommand.Execute(null); break;
                        case Key.F5: vm.AddDiscountCommand.Execute(null); break;
                        case Key.F6: vm.HoldTransactionCommand.Execute(null); break;
                        case Key.F7: vm.RecallTransactionCommand.Execute(null); break;
                        case Key.F8: vm.ProcessReturnCommand.Execute(null); break;
                        case Key.F9: vm.ReprintLastCommand.Execute(null); break;
                        case Key.F10: vm.ClearTransactionCommand.Execute(null); break;
                        case Key.F12: vm.CashPaymentCommand.Execute(null); break;
                        case Key.Escape: vm.CancelTransactionCommand.Execute(null); break;
                    }
                };
            }
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
            if (sender is TextBox textBox &&
                textBox.DataContext is TransactionDetailDTO detail)
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
        private void ProductSearchGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is DataGrid grid && grid.SelectedItem is ProductDTO selectedProduct)
            {
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.OnProductSelected(selectedProduct);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                var viewModel = DataContext as TransactionViewModel;
                if (viewModel != null)
                {
                    viewModel.IsProductSearchVisible = false;
                }
                e.Handled = true;
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void ProductSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void ProductComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}