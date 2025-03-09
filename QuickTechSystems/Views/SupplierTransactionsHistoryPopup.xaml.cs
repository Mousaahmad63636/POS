using System.Windows;
using System.Windows.Controls;
using QuickTechSystems.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class SupplierTransactionsHistoryPopup : UserControl
    {
        public event RoutedEventHandler CloseRequested;

        public SupplierTransactionsHistoryPopup()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, new RoutedEventArgs());
        }

        private void AddPaymentButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierViewModel viewModel)
            {
                // Close history popup and open transaction popup
                viewModel.CloseTransactionsHistoryPopup();
                viewModel.ShowTransactionPopup();
            }
        }
    }
}