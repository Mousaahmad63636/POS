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
        // Column indices for responsive visibility (based on DataGrid columns)
        private const int TOTAL_COST_COLUMN_INDEX = 7;
        private const int TOTAL_VALUE_COLUMN_INDEX = 8;
        private const int TOTAL_PROFIT_COLUMN_INDEX = 9;
        private const int SPEED_COLUMN_INDEX = 10;
        private const int SUPPLIER_COLUMN_INDEX = 11;

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
            double windowHeight = parentWindow.ActualHeight;

            // Automatically adjust the layout based on the window size
            SetResponsiveLayout(windowWidth, windowHeight);
        }

        private void SetResponsiveLayout(double width, double height)
        {
            // Get content grid
            if (ContentGrid == null) return;

            // Adaptive margin based on screen size
            double marginSize = CalculateAdaptiveMargin(width);
            ContentGrid.Margin = new Thickness(marginSize);

            // Determine which columns to show based on available width
            bool showTotalCostColumn = width >= 1100;
            bool showTotalValueColumn = width >= 1200;
            bool showTotalProfitColumn = width >= 1000; // Keep profit visible on most screens
            bool showSpeedColumn = width >= 1400;
            bool showSupplierColumn = width >= 1300;

            // Apply column visibility
            SetColumnVisibility(showTotalCostColumn, showTotalValueColumn,
                showTotalProfitColumn, showSpeedColumn, showSupplierColumn);
        }

        private double CalculateAdaptiveMargin(double screenWidth)
        {
            // Calculate margin as a percentage of screen width
            double marginPercentage = 0.02; // 2% of screen width
            double calculatedMargin = screenWidth * marginPercentage;

            // Ensure margin stays within reasonable bounds
            return Math.Max(8, Math.Min(32, calculatedMargin));
        }

        private void SetColumnVisibility(bool showTotalCostColumn, bool showTotalValueColumn,
            bool showTotalProfitColumn, bool showSpeedColumn, bool showSupplierColumn)
        {
            if (ProductsDataGrid == null || ProductsDataGrid.Columns.Count == 0)
                return;

            // Safely check if we have enough columns
            if (ProductsDataGrid.Columns.Count > TOTAL_COST_COLUMN_INDEX)
                ProductsDataGrid.Columns[TOTAL_COST_COLUMN_INDEX].Visibility =
                    showTotalCostColumn ? Visibility.Visible : Visibility.Collapsed;

            if (ProductsDataGrid.Columns.Count > TOTAL_VALUE_COLUMN_INDEX)
                ProductsDataGrid.Columns[TOTAL_VALUE_COLUMN_INDEX].Visibility =
                    showTotalValueColumn ? Visibility.Visible : Visibility.Collapsed;

            if (ProductsDataGrid.Columns.Count > TOTAL_PROFIT_COLUMN_INDEX)
                ProductsDataGrid.Columns[TOTAL_PROFIT_COLUMN_INDEX].Visibility =
                    showTotalProfitColumn ? Visibility.Visible : Visibility.Collapsed;

            if (ProductsDataGrid.Columns.Count > SPEED_COLUMN_INDEX)
                ProductsDataGrid.Columns[SPEED_COLUMN_INDEX].Visibility =
                    showSpeedColumn ? Visibility.Visible : Visibility.Collapsed;

            if (ProductsDataGrid.Columns.Count > SUPPLIER_COLUMN_INDEX)
                ProductsDataGrid.Columns[SUPPLIER_COLUMN_INDEX].Visibility =
                    showSupplierColumn ? Visibility.Visible : Visibility.Collapsed;
        }

        // Preserve all existing event handlers
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is ProductDTO product &&
                DataContext is ProductViewModel viewModel)
            {
                viewModel.SelectedProduct = product;
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