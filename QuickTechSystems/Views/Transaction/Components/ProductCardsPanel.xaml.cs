using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.WPF.Services;

namespace QuickTechSystems.WPF.Views.Transaction.Components
{
    public partial class ProductCardsPanel : UserControl
    {
        public ProductCardsPanel()
        {
            InitializeComponent();
        }

        private void Product_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProductDTO product)
            {
                ShowQuantityDialog(product);
            }
        }

        private void ShowQuantityDialog(ProductDTO product)
        {
            if (InputDialogService.TryGetDecimalInput("Enter Quantity", product.Name, 1, out decimal quantity, Window.GetWindow(this)))
            {
                // Use the view model to add the product with quantity
                var viewModel = this.DataContext as TransactionViewModel;
                if (viewModel != null)
                {
                    viewModel.AddProductToTransaction(product, quantity);
                }
            }
        }
    }
}