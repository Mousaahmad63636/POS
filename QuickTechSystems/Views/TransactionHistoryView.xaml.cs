using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionHistoryView : UserControl
    {
        // Column indices for responsive visibility
        private const int TYPE_COLUMN_INDEX = 3;
        private const int ITEMS_COLUMN_INDEX = 4;
        private const int STATUS_COLUMN_INDEX = 6;
        private const int CASHIER_COLUMN_INDEX = 7;
        private const int ROLE_COLUMN_INDEX = 8;

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
            double windowHeight = parentWindow.ActualHeight;

            // Automatically adjust the layout based on the window size
            // instead of using fixed breakpoints
            SetResponsiveLayout(windowWidth, windowHeight);
        }

        private void SetResponsiveLayout(double width, double height)
        {
            // Adaptive margin based on screen size
            double marginSize = CalculateAdaptiveMargin(width);
            ContentGrid.Margin = new Thickness(marginSize);

            // Determine which columns to show based on available width
            bool showTypeColumn = width >= 800;
            bool showStatusColumn = width >= 1000;
            bool showCashierColumn = width >= 1200;
            bool showRoleColumn = width >= 1400;

            // Apply column visibility
            SetColumnVisibility(showTypeColumn, showStatusColumn, showCashierColumn, showRoleColumn);
        }

        private double CalculateAdaptiveMargin(double screenWidth)
        {
            // Calculate margin as a percentage of screen width
            double marginPercentage = 0.02; // 2% of screen width
            double calculatedMargin = screenWidth * marginPercentage;

            // Ensure margin stays within reasonable bounds
            return Math.Max(8, Math.Min(32, calculatedMargin));
        }

        private void SetColumnVisibility(bool showTypeColumn, bool showStatusColumn, bool showCashierColumn, bool showRoleColumn)
        {
            if (TransactionsDataGrid == null || TransactionsDataGrid.Columns.Count == 0)
                return;

            // Safely check if we have enough columns
            if (TransactionsDataGrid.Columns.Count > TYPE_COLUMN_INDEX)
                TransactionsDataGrid.Columns[TYPE_COLUMN_INDEX].Visibility = showTypeColumn ? Visibility.Visible : Visibility.Collapsed;

            if (TransactionsDataGrid.Columns.Count > STATUS_COLUMN_INDEX)
                TransactionsDataGrid.Columns[STATUS_COLUMN_INDEX].Visibility = showStatusColumn ? Visibility.Visible : Visibility.Collapsed;

            if (TransactionsDataGrid.Columns.Count > CASHIER_COLUMN_INDEX)
                TransactionsDataGrid.Columns[CASHIER_COLUMN_INDEX].Visibility = showCashierColumn ? Visibility.Visible : Visibility.Collapsed;

            if (TransactionsDataGrid.Columns.Count > ROLE_COLUMN_INDEX)
                TransactionsDataGrid.Columns[ROLE_COLUMN_INDEX].Visibility = showRoleColumn ? Visibility.Visible : Visibility.Collapsed;

            // Items column visibility based on other columns
            if (TransactionsDataGrid.Columns.Count > ITEMS_COLUMN_INDEX)
                TransactionsDataGrid.Columns[ITEMS_COLUMN_INDEX].Visibility = (showTypeColumn && showStatusColumn)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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