using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionHistoryView : UserControl
    {
        public TransactionHistoryView()
        {
            InitializeComponent();
            this.Loaded += TransactionHistoryView_Loaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        private void TransactionHistoryView_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure DataContext is set if using DI
            if (DataContext is TransactionHistoryViewModel viewModel)
            {
                // Any initialization if needed
            }

            AdjustLayoutForSize();
        }

        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustLayoutForSize();
        }

        private void AdjustLayoutForSize()
        {
            var parentWindow = Window.GetWindow(this);
            if (parentWindow == null) return;

            // Get actual window dimensions
            double windowWidth = parentWindow.ActualWidth;

            // Set margins and paddings based on window size
            var scrollViewer = this.Content as Grid;
            if (scrollViewer == null) return;

            var rootGrid = scrollViewer.Children[0] as ScrollViewer;
            if (rootGrid == null) return;

            var contentGrid = rootGrid.Content as Grid;
            if (contentGrid == null) return;

            if (windowWidth >= 1920) // Large screens
            {
                contentGrid.Margin = new Thickness(32);
            }
            else if (windowWidth >= 1366) // Medium screens
            {
                contentGrid.Margin = new Thickness(24);
            }
            else if (windowWidth >= 800) // Small screens
            {
                contentGrid.Margin = new Thickness(16);
            }
            else // Very small screens
            {
                contentGrid.Margin = new Thickness(8);
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is TransactionDTO transaction)
            {
                if (DataContext is TransactionHistoryViewModel viewModel)
                {
                    viewModel.ViewTransactionDetailsCommand.Execute(transaction);
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && DataContext is TransactionHistoryViewModel viewModel)
            {
                viewModel.SearchText = textBox.Text;
            }
        }
    }
}