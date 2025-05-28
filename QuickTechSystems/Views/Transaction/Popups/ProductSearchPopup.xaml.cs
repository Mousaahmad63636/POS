using System.Windows.Controls;
using System.Windows.Input;

namespace QuickTechSystems.WPF.Views.Transaction.Popups
{
    public partial class ProductSearchPopup : UserControl
    {
        public ProductSearchPopup()
        {
            InitializeComponent();
        }

        private void ProductSearchGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ProductDTO selectedProduct)
            {
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.OnProductSelected(selectedProduct);
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
        }
    }
}