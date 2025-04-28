using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.Services;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionView : UserControl
    {
        // Fields to cache UI elements for performance
        private Grid mainContentGrid;
        private Border mainPanel;
        private Grid mainContent;
        private bool elementsInitialized = false;
        private System.Timers.Timer _resizeTimer;
        private const int ResizeDelay = 150; // milliseconds

        public TransactionView()
        {
            InitializeComponent();
            SetupKeyboardShortcuts();

            // Ensure we're handling size changes
            this.Loaded += TransactionView_Loaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        #region Event Handlers

        private void TransactionView_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure DataContext is set properly
            if (DataContext is TransactionViewModel viewModel)
            {
                // Force refresh of restaurant mode
                viewModel.LoadRestaurantModePreference().ConfigureAwait(false);

                // Load the latest transaction ID for easy lookup
                viewModel.LoadLatestTransactionIdAsync().ConfigureAwait(false);
            }

            // Find and cache UI elements
            InitializeUIElements();

            // Adjust layout based on size
            AdjustLayoutForSize();

            // Also attach a size changed handler to the parent window
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null && !parentWindow.Tag?.ToString().Contains("ResizeHandlerAttached") == true)
            {
                parentWindow.SizeChanged += ParentWindow_SizeChanged;
                parentWindow.Tag = "ResizeHandlerAttached";
            }
        }

        private void ParentWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Use the timer-based approach
            if (_resizeTimer == null)
            {
                _resizeTimer = new System.Timers.Timer(ResizeDelay);
                _resizeTimer.Elapsed += (s, args) =>
                {
                    _resizeTimer.Stop();
                    Dispatcher.Invoke(AdjustLayoutForSize);
                };
            }
            else
            {
                _resizeTimer.Stop();
            }

            _resizeTimer.Start();
        }

        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Use a timer to debounce resize events
            if (_resizeTimer == null)
            {
                _resizeTimer = new System.Timers.Timer(ResizeDelay);
                _resizeTimer.Elapsed += (s, args) =>
                {
                    _resizeTimer.Stop();
                    Dispatcher.Invoke(AdjustLayoutForSize);
                };
            }
            else
            {
                _resizeTimer.Stop();
            }

            _resizeTimer.Start();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                foreach (TransactionDetailDTO item in dataGrid.Items)
                {
                    item.IsSelected = dataGrid.SelectedItems.Contains(item);
                }
            }
        }

        private void PriceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is TransactionDetailDTO detail)
            {
                ShowPriceKeypad(detail);
            }
        }

        private void Quantity_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is TransactionDetailDTO detail)
            {
                ShowQuantityKeypad(detail);
                e.Handled = true;
            }
        }

        private void Quantity_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow digits and at most one decimal point
            if (!char.IsDigit(e.Text[0]) && e.Text[0] != '.')
            {
                e.Handled = true;
                return;
            }

            // Special handling for decimal point
            if (e.Text[0] == '.')
            {
                if (sender is TextBox textBox && textBox.Text.Contains("."))
                {
                    // Already has a decimal point, prevent adding another
                    e.Handled = true;
                    return;
                }
            }
        }

        private void Quantity_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is TransactionDetailDTO detail)
            {
                // Parse the quantity
                if (decimal.TryParse(textBox.Text,
                                     NumberStyles.Number | NumberStyles.AllowDecimalPoint,
                                     CultureInfo.InvariantCulture,
                                     out decimal newQuantity))
                {
                    // Valid quantity
                    if (newQuantity <= 0)
                    {
                        MessageBox.Show(
                            "Quantity must be positive. Value reset to original quantity.",
                            "Invalid Quantity",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        // Reset to original value
                        textBox.Text = detail.Quantity.ToString();
                        return;
                    }

                    // Update quantity in data model
                    detail.Quantity = newQuantity;
                    detail.Total = detail.UnitPrice * newQuantity;

                    // Update totals in view model
                    var viewModel = DataContext as TransactionViewModel;
                    viewModel?.UpdateTotals();
                }
                else
                {
                    // Invalid quantity
                    MessageBox.Show(
                        "Please enter a valid quantity. Value reset to original quantity.",
                        "Invalid Quantity",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    // Reset to original value
                    textBox.Text = detail.Quantity.ToString();
                }
            }
        }

        private void CustomerCancelButton_Click(object sender, RoutedEventArgs e)
        {
            SafeExecute(async () => {
                if (CustomerSearchBox != null)
                {
                    CustomerSearchBox.Text = string.Empty;

                    // Get the view model
                    if (DataContext is TransactionViewModel viewModel)
                    {
                        // Execute the clear command
                        if (viewModel.ClearCustomerCommand.CanExecute(null))
                        {
                            viewModel.ClearCustomerCommand.Execute(null);
                        }
                    }
                }
            });
        }

        private void Product_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProductDTO product)
            {
                ShowQuantityDialog(product);
            }
        }

        private void ProductSearchGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ProductDTO selectedProduct)
            {
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.OnProductSelected(selectedProduct);
            }
        }

        private void ProductSearchGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is DataGrid grid && grid.SelectedItem is ProductDTO selectedProduct)
            {
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.OnProductSelected(selectedProduct);
                e.Handled = true;
            }
        }

        #endregion

        #region Utilities and Helpers

        private void SafeExecute(Func<Task> action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"An unexpected error occurred: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void InitializeUIElements()
        {
            if (elementsInitialized) return;

            // Get the main grid (the root element of the UserControl)
            if (!(this.Content is Grid rootGrid)) return;

            // Main content grid (the one containing the main panel)
            if (rootGrid.Children.Count < 2 || !(rootGrid.Children[1] is Border mainBorder)) return;
            mainPanel = mainBorder;

            // Main panel content
            if (mainBorder.Child is Grid gridContent)
                mainContent = gridContent;

            elementsInitialized = true;
        }

        private void AdjustLayoutForSize()
        {
            var parentWindow = Window.GetWindow(this);
            if (parentWindow == null) return;

            // Get actual window dimensions
            double windowWidth = parentWindow.ActualWidth;
            double windowHeight = parentWindow.ActualHeight;

            // If elements aren't initialized or we've lost references, try again
            if (!elementsInitialized || mainPanel == null)
            {
                InitializeUIElements();
                if (!elementsInitialized) return;
            }

            // Apply size-based adjustments
            AdjustMargins(windowWidth);
            AdjustContentPadding(windowWidth);
            AdjustFunctionButtonsForSize(windowWidth);

            // Check if window is too small and show warning
            const double minimumWindowWidth = 800;
            const double minimumWindowHeight = 600;

            if (windowWidth < minimumWindowWidth || windowHeight < minimumWindowHeight)
            {
                // Update status message via the view model
                if (DataContext is TransactionViewModel viewModel)
                {
                    viewModel.StatusMessage = "Warning: Window size is too small for optimal display";
                }
            }
            else
            {
                // Reset status message when window is large enough
                if (DataContext is TransactionViewModel viewModel)
                {
                    viewModel.StatusMessage = "Ready";
                }
            }
        }

        private void AdjustFunctionButtonsForSize(double windowWidth)
        {
            // Use the named grid from XAML
            if (functionButtonsGrid == null) return;

            if (windowWidth < 1200)
            {
                // Switch to 2 rows x 5 columns for smaller screens
                functionButtonsGrid.Rows = 2;
                functionButtonsGrid.Columns = 5;
            }
            else
            {
                // Use default 1 row x 10 columns for larger screens
                functionButtonsGrid.Rows = 1;
                functionButtonsGrid.Columns = 10;
            }
        }

        private void AdjustMargins(double windowWidth)
        {
            if (mainPanel == null) return;

            Thickness margin;

            if (windowWidth >= 1920)
            {
                margin = new Thickness(20, 20, 20, 20);
            }
            else if (windowWidth >= 1366)
            {
                margin = new Thickness(16, 16, 16, 16);
            }
            else if (windowWidth >= 800)
            {
                margin = new Thickness(12, 12, 12, 12);
            }
            else
            {
                margin = new Thickness(8, 8, 8, 8);
            }

            mainPanel.Margin = margin;
        }

        private void AdjustContentPadding(double windowWidth)
        {
            if (mainContent == null) return;

            Thickness contentPadding;

            if (windowWidth >= 1366)
                contentPadding = new Thickness(20);
            else if (windowWidth >= 800)
                contentPadding = new Thickness(16);
            else
                contentPadding = new Thickness(12);

            mainContent.Margin = contentPadding;
        }

        private void SetupKeyboardShortcuts()
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                // Clean up any existing handler before adding a new one
                if (_keyDownHandler != null)
                {
                    window.KeyDown -= _keyDownHandler;
                }

                // Store the handler reference so we can remove it later
                _keyDownHandler = (s, e) =>
                {
                    // Skip handling if we're in a TextBox
                    if (e.OriginalSource is TextBox) return;

                    var vm = DataContext as TransactionViewModel;
                    if (vm == null) return;

                    switch (e.Key)
                    {
                        case Key.F2: vm.VoidLastItemCommand.Execute(null); e.Handled = true; break;
                        case Key.F3: vm.ChangeQuantityCommand.Execute(null); e.Handled = true; break;
                        case Key.F4: vm.PriceCheckCommand.Execute(null); e.Handled = true; break;
                        case Key.F5: vm.AddDiscountCommand.Execute(null); e.Handled = true; break;
                        case Key.F6: vm.HoldTransactionCommand.Execute(null); e.Handled = true; break;
                        case Key.F7: vm.RecallTransactionCommand.Execute(null); e.Handled = true; break;
                        case Key.F9: vm.ReprintLastCommand.Execute(null); e.Handled = true; break;
                        case Key.F10: vm.ClearTransactionCommand.Execute(null); e.Handled = true; break;
                        case Key.F12: vm.CashPaymentCommand.Execute(null); e.Handled = true; break;
                        case Key.Escape: vm.CancelTransactionCommand.Execute(null); e.Handled = true; break;
                        case Key.V:
                            if (Keyboard.Modifiers == ModifierKeys.Control)
                            {
                                vm.ToggleViewCommand.Execute(null);
                                e.Handled = true;
                            }
                            break;
                    }
                };

                window.KeyDown += _keyDownHandler;

                // Clean up when control is unloaded
                this.Unloaded += (s, e) => {
                    if (window != null)
                        window.KeyDown -= _keyDownHandler;

                    // Dispose of timer resources
                    if (_resizeTimer != null)
                    {
                        _resizeTimer.Stop();
                        _resizeTimer.Dispose();
                        _resizeTimer = null;
                    }
                };
            }
        }

        private KeyEventHandler _keyDownHandler;

        #endregion

        #region Dialog Helpers

        private void ShowQuantityKeypad(TransactionDetailDTO detail)
        {
            if (InputDialogService.TryGetDecimalInput("Change Quantity", detail.ProductName, detail.Quantity, out decimal newQuantity, Window.GetWindow(this)))
            {
                // Update quantity in data model
                detail.Quantity = newQuantity;
                detail.Total = detail.UnitPrice * newQuantity;

                // Update totals in view model
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.UpdateTotals();
            }
        }

        private void ShowPriceKeypad(TransactionDetailDTO detail)
        {
            if (InputDialogService.TryGetDecimalInput("Change Price", detail.ProductName, detail.UnitPrice, out decimal newPrice, Window.GetWindow(this)))
            {
                // Get the view model
                var viewModel = DataContext as TransactionViewModel;
                if (viewModel == null) return;

                // Get the cost price for the product
                decimal costPrice = viewModel.GetProductCostPrice(detail.ProductId);

                // Only allow prices equal to or higher than cost price
                if (newPrice < costPrice)
                {
                    MessageBox.Show(
                        "New price cannot be lower than cost price.",
                        "Invalid Price",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Update price in data model
                detail.UnitPrice = newPrice;
                detail.Total = detail.UnitPrice * detail.Quantity;

                // Update totals in view model
                viewModel.UpdateTotals();
            }
        }

        private void ShowQuantityDialog(ProductDTO product)
        {
            if (InputDialogService.TryGetDecimalInput("Enter Quantity", product.Name, 1, out decimal quantity, Window.GetWindow(this)))
            {
                // Use the view model to add the product with quantity
                var viewModel = DataContext as TransactionViewModel;
                if (viewModel != null)
                {
                    viewModel.AddProductToTransaction(product, quantity);
                }
            }
        }

        #endregion
    }
}