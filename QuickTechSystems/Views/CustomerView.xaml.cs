using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class CustomerView : UserControl
    {
        public CustomerView()
        {
            InitializeComponent();
            this.Loaded += CustomerView_Loaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        private void CustomerView_Loaded(object sender, RoutedEventArgs e)
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
            if (sender is DataGrid grid && grid.SelectedItem is CustomerDTO customer &&
                DataContext is CustomerViewModel viewModel)
            {
                viewModel.EditCustomer(customer);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is CustomerDTO customer &&
                DataContext is CustomerViewModel viewModel)
            {
                viewModel.EditCustomer(customer);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is CustomerDTO customer &&
                DataContext is CustomerViewModel viewModel)
            {
                // Set the SelectedCustomer first, then execute the DeleteCommand
                viewModel.SelectedCustomer = customer;
                if (viewModel.DeleteCommand.CanExecute(null))
                {
                    viewModel.DeleteCommand.Execute(null);
                }
            }
        }

        // Context menu handlers
        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is CustomerViewModel viewModel &&
                viewModel.SelectedCustomer != null)
            {
                viewModel.EditCustomer(viewModel.SelectedCustomer);
            }
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is CustomerViewModel viewModel &&
                viewModel.SelectedCustomer != null &&
                viewModel.DeleteCommand.CanExecute(null))
            {
                viewModel.DeleteCommand.Execute(null);
            }
        }

        // Popup event handlers
        private void CustomerDetailsPopup_CloseRequested(object sender, RoutedEventArgs e)
        {
            if (DataContext is CustomerViewModel viewModel)
            {
                viewModel.CloseCustomerPopup();
            }
        }

        private void CustomerDetailsPopup_SaveCompleted(object sender, RoutedEventArgs e)
        {
            if (DataContext is CustomerViewModel viewModel)
            {
                viewModel.CloseCustomerPopup();
            }
        }

        // Product prices helpers
        private void ResetPrice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is CustomerProductPriceViewModel priceModel)
            {
                priceModel.CustomPrice = priceModel.DefaultPrice;
            }
        }
    }
}