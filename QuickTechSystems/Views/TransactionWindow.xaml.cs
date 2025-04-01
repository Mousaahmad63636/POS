// Path: QuickTechSystems.WPF/Views/TransactionWindow.xaml.cs
using System;
using System.ComponentModel;
using System.Windows;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionWindow : Window
    {
        private readonly TransactionViewModel _viewModel;

        public TransactionWindow(TransactionViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            // Set window title
            Title = $"Transaction #{DateTime.Now.ToString("yyyyMMddHHmmss")}";

            // Handle window closing to properly dispose resources
            Closing += TransactionWindow_Closing;
        }

        private void TransactionWindow_Closing(object sender, CancelEventArgs e)
        {
            // Check if there's an active transaction with items
            if (_viewModel?.CurrentTransaction?.Details?.Count > 0)
            {
                var result = MessageBox.Show(
                    "You have an active transaction. Are you sure you want to close this window?",
                    "Confirm Close",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // Properly dispose of the view model when the window is closed
            _viewModel?.Dispose();
        }
    }
}