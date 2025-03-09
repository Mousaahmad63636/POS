using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class ExpenseView : UserControl
    {
        public ExpenseView()
        {
            InitializeComponent();
            this.Loaded += ExpenseView_Loaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        private void ExpenseView_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure DataContext is set properly
            if (DataContext != null)
            {
                // Any initialization needed
            }

            // Adjust layout based on size
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

        // Popup event handlers
        private void ExpenseDetailsPopup_CloseRequested(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExpenseViewModel viewModel)
            {
                viewModel.CloseExpensePopup();
            }
        }

        private void ExpenseDetailsPopup_SaveCompleted(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExpenseViewModel viewModel)
            {
                viewModel.CloseExpensePopup();
            }
        }

        // DataGrid event handlers
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ExpenseDTO expense &&
                DataContext is ExpenseViewModel viewModel)
            {
                viewModel.EditCommand.Execute(expense);
            }
        }

        // Context menu event handlers
        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExpenseViewModel viewModel &&
                viewModel.SelectedExpense != null)
            {
                viewModel.EditCommand.Execute(viewModel.SelectedExpense);
            }
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExpenseViewModel viewModel &&
                viewModel.SelectedExpense != null)
            {
                viewModel.DeleteCommand.Execute(viewModel.SelectedExpense);
            }
        }
    }
}