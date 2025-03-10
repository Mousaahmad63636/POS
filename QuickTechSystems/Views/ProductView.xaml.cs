using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class ProductView : UserControl
    {
        public ProductView()
        {
            InitializeComponent();
            this.Loaded += ProductView_Loaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        private void ProductView_Loaded(object sender, RoutedEventArgs e)
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
            var scrollViewer = this.Content as ScrollViewer;
            if (scrollViewer == null) return;

            var tabControl = scrollViewer.Content as TabControl;
            if (tabControl == null) return;

            if (windowWidth >= 1920) // Large screens
            {
                tabControl.Margin = new Thickness(32);
            }
            else if (windowWidth >= 1366) // Medium screens
            {
                tabControl.Margin = new Thickness(24);
            }
            else if (windowWidth >= 800) // Small screens
            {
                tabControl.Margin = new Thickness(16);
            }
            else // Very small screens
            {
                tabControl.Margin = new Thickness(8);
            }
        }
        // Add this method to ProductView.xaml.cs
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is ProductDTO product &&
                DataContext is ProductViewModel viewModel)
            {
                // Set the selected product before executing delete command
                viewModel.SelectedProduct = product;

                // Execute the delete command if it can execute
                if (viewModel.DeleteCommand.CanExecute(null))
                {
                    viewModel.DeleteCommand.Execute(null);
                }
            }
        }
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid &&
                grid.SelectedItem is ProductDTO product &&
                DataContext is ProductViewModel viewModel)
            {
                viewModel.EditProduct(product);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is ProductDTO product &&
                DataContext is ProductViewModel viewModel)
            {
                viewModel.EditProduct(product);
            }
        }

        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProductViewModel viewModel &&
                viewModel.SelectedProduct != null)
            {
                viewModel.EditProduct(viewModel.SelectedProduct);
            }
        }

        private void ProductDetailsPopup_CloseRequested(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProductViewModel viewModel)
            {
                viewModel.CloseProductPopup();
            }
        }

        private void ProductDetailsPopup_SaveCompleted(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProductViewModel viewModel)
            {
                viewModel.CloseProductPopup();
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl)
            {
                // Check if we're selecting the Damaged Goods tab
                if (tabControl.SelectedIndex == 1 && DamagedGoodsContent != null)
                {
                    // Load the DamagedGoodsView dynamically if not already loaded
                    if (!(DamagedGoodsContent.Content is DamagedGoodsView))
                    {
                        try
                        {
                            var app = (App)System.Windows.Application.Current;
                            var damagedGoodsViewModel = app.ServiceProvider.GetService(typeof(DamagedGoodsViewModel)) as DamagedGoodsViewModel;
                            var damagedGoodsView = new DamagedGoodsView();
                            damagedGoodsView.DataContext = damagedGoodsViewModel;
                            DamagedGoodsContent.Content = damagedGoodsView;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading DamagedGoodsView: {ex.Message}");
                        }
                    }
                }
            }
        }
    }
}