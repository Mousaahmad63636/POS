using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class SupplierView : UserControl
    {
        public SupplierView()
        {
            InitializeComponent();
            this.Loaded += OnControlLoaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
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
            var scrollViewer = MainGrid.Children[0] as ScrollViewer;
            if (scrollViewer == null) return;

            var contentGrid = scrollViewer.Content as Grid;
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

        // Event handlers for DataGrid
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is SupplierDTO supplier &&
                DataContext is SupplierViewModel viewModel)
            {
                viewModel.EditSupplier(supplier);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is SupplierDTO supplier &&
                DataContext is SupplierViewModel viewModel)
            {
                viewModel.EditSupplier(supplier);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is SupplierDTO supplier &&
                DataContext is SupplierViewModel viewModel)
            {
                viewModel.SelectedSupplier = supplier;
                if (viewModel.DeleteCommand.CanExecute(null))
                {
                    viewModel.DeleteCommand.Execute(null);
                }
            }
        }

        private void AddTransactionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is SupplierDTO supplier &&
                DataContext is SupplierViewModel viewModel)
            {
                viewModel.SelectedSupplier = supplier;
                viewModel.ShowTransactionPopup();
            }
        }

        private void ViewHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is SupplierDTO supplier &&
                DataContext is SupplierViewModel viewModel)
            {
                viewModel.SelectedSupplier = supplier;
                viewModel.ShowTransactionsHistoryPopup();
            }
        }

        // Context menu handlers
        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierViewModel viewModel &&
                viewModel.SelectedSupplier != null)
            {
                viewModel.EditSupplier(viewModel.SelectedSupplier);
            }
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierViewModel viewModel &&
                viewModel.SelectedSupplier != null &&
                viewModel.DeleteCommand.CanExecute(null))
            {
                viewModel.DeleteCommand.Execute(null);
            }
        }

        private void AddTransactionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierViewModel viewModel &&
                viewModel.SelectedSupplier != null)
            {
                viewModel.ShowTransactionPopup();
            }
        }

        private void ViewTransactionsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierViewModel viewModel &&
                viewModel.SelectedSupplier != null)
            {
                viewModel.ShowTransactionsHistoryPopup();
            }
        }

        // Popup event handlers
        private void SupplierDetailsPopup_CloseRequested(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierViewModel viewModel)
            {
                viewModel.CloseSupplierPopup();
            }
        }

        private void SupplierDetailsPopup_SaveCompleted(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierViewModel viewModel)
            {
                viewModel.CloseSupplierPopup();
            }
        }

        private void SupplierTransactionPopup_CloseRequested(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierViewModel viewModel)
            {
                viewModel.CloseTransactionPopup();
            }
        }

        private void SupplierTransactionPopup_SaveCompleted(object sender, RoutedEventArgs e)
        {
            // The transaction will be saved and UI will be handled by the ViewModel
            // No additional handling needed here as the ViewModel manages popup state
        }

        private void SupplierTransactionsHistoryPopup_CloseRequested(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupplierViewModel viewModel)
            {
                viewModel.CloseTransactionsHistoryPopup();
            }
        }
    }
}