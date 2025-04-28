// Path: QuickTechSystems.WPF.Views/MainStockView.xaml.cs
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class MainStockView : UserControl
    {
        public MainStockView()
        {
            InitializeComponent();
            this.Loaded += MainStockView_Loaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        private void MainStockView_Loaded(object sender, RoutedEventArgs e)
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
        }

        private double CalculateAdaptiveMargin(double screenWidth)
        {
            // Calculate margin as a percentage of screen width
            double marginPercentage = 0.02; // 2% of screen width
            double calculatedMargin = screenWidth * marginPercentage;

            // Ensure margin stays within reasonable bounds
            return Math.Max(8, Math.Min(32, calculatedMargin));
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid &&
                grid.SelectedItem is MainStockDTO item &&
                DataContext is MainStockViewModel viewModel)
            {
                viewModel.EditItem(item);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is MainStockDTO item &&
                DataContext is MainStockViewModel viewModel)
            {
                viewModel.EditItem(item);
            }
        }

        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainStockViewModel viewModel &&
                viewModel.SelectedItem != null)
            {
                viewModel.EditItem(viewModel.SelectedItem);
            }
        }

        private void CloseTransferPopup_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainStockViewModel viewModel)
            {
                viewModel.IsTransferPopupOpen = false;
            }
        }
    }
}