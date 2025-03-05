using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionHistoryView : UserControl
    {
        public TransactionHistoryView()
        {
            InitializeComponent();
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is TransactionDTO transaction)
            {
                try
                {
                    var viewModel = DataContext as TransactionHistoryViewModel;
                    if (viewModel != null)
                    {
                        viewModel.ViewTransactionDetailsCommand.Execute(transaction);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error showing transaction details: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}