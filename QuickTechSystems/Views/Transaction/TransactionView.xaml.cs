using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.Services;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.WPF.Views.Dialogs;

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
        private void NewTransactionTable_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("New Table Transaction", "Enter table number or leave blank for auto-assigned:");
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                string tableId = dialog.Input?.Trim();
                TransactionWindowManager.Instance.CreateNewTransactionWindow(tableId);
            }
        }
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

                // Update statusBar appearance if it exists
                if (statusBar != null)
                {
                    statusBar.Background = new SolidColorBrush(Colors.DarkOrange);
                    statusBar.Foreground = new SolidColorBrush(Colors.White);
                }
            }
            else
            {
                // Reset status message when window is large enough
                if (DataContext is TransactionViewModel viewModel)
                {
                    viewModel.StatusMessage = "Ready";
                }

                // Reset statusBar appearance
                if (statusBar != null)
                {
                    statusBar.ClearValue(Control.BackgroundProperty);
                    statusBar.ClearValue(Control.ForegroundProperty);
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

        private void QuantityButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is TransactionDetailDTO detail)
            {
                ShowQuantityKeypad(detail);
            }
        }

        private void ShowQuantityKeypad(TransactionDetailDTO detail)
        {
            var window = new Window
            {
                Title = "Change Quantity",
                Width = 300,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White
            };

            // Main grid
            var mainGrid = new Grid { Margin = new Thickness(10) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Product name
            var productName = new TextBlock
            {
                Text = detail.ProductName,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(productName, 0);
            mainGrid.Children.Add(productName);

            // Quantity display
            var displayBorder = new Border
            {
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(displayBorder, 1);

            var quantityText = new TextBlock
            {
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right,
                Padding = new Thickness(10, 5, 10, 5),
                Text = detail.Quantity.ToString()
            };
            displayBorder.Child = quantityText;
            mainGrid.Children.Add(displayBorder);

            // Keypad
            var keypadGrid = new Grid();
            for (int i = 0; i < 4; i++)
                keypadGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++)
                keypadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(keypadGrid, 2);
            mainGrid.Children.Add(keypadGrid);

            // Add number buttons
            AddButton(keypadGrid, "7", 0, 0);
            AddButton(keypadGrid, "8", 0, 1);
            AddButton(keypadGrid, "9", 0, 2);
            AddButton(keypadGrid, "4", 1, 0);
            AddButton(keypadGrid, "5", 1, 1);
            AddButton(keypadGrid, "6", 1, 2);
            AddButton(keypadGrid, "1", 2, 0);
            AddButton(keypadGrid, "2", 2, 1);
            AddButton(keypadGrid, "3", 2, 2);
            AddButton(keypadGrid, "0", 3, 0);
            AddButton(keypadGrid, "00", 3, 1);

            // Clear button
            var clearButton = AddButton(keypadGrid, "C", 3, 2);
            clearButton.Click += (s, e) => { quantityText.Text = "0"; };

            // Action buttons
            var actionGrid = new Grid();
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(actionGrid, 3);
            actionGrid.Margin = new Thickness(0, 15, 0, 0);
            mainGrid.Children.Add(actionGrid);

            // Cancel button
            var cancelButton = new Button
            {
                Content = "Cancel",
                Margin = new Thickness(3, 0, 3, 0),
                Padding = new Thickness(0, 10, 0, 10),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")),
                Foreground = Brushes.White
            };
            Grid.SetColumn(cancelButton, 0);
            actionGrid.Children.Add(cancelButton);
            cancelButton.Click += (s, e) => { window.DialogResult = false; };

            // Update button
            var updateButton = new Button
            {
                Content = "Update",
                Margin = new Thickness(3, 0, 3, 0),
                Padding = new Thickness(0, 10, 0, 10),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                Foreground = Brushes.White
            };
            Grid.SetColumn(updateButton, 1);
            actionGrid.Children.Add(updateButton);
            updateButton.Click += (s, e) =>
            {
                if (int.TryParse(quantityText.Text, out int qty) && qty > 0)
                {
                    window.DialogResult = true;
                }
                else
                {
                    MessageBox.Show("Please enter a valid quantity.", "Invalid Quantity",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            window.Content = mainGrid;

            // Show dialog and process result
            if (window.ShowDialog() == true)
            {
                int newQuantity = int.Parse(quantityText.Text);

                // Update quantity in data model
                detail.Quantity = newQuantity;
                detail.Total = detail.UnitPrice * newQuantity;

                // Update totals in view model
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.UpdateTotals();
            }
        }

        private Button AddButton(Grid grid, string text, int row, int col)
        {
            var button = new Button
            {
                Content = text,
                FontSize = 20,
                Margin = new Thickness(3)
            };
            Grid.SetRow(button, row);
            Grid.SetColumn(button, col);
            grid.Children.Add(button);

            // Add click handler for number buttons
            if (text != "C")
            {
                button.Click += (s, e) =>
                {
                    var textBlock = FindVisualChild<TextBlock>((Border)((Grid)grid.Parent).Children[1]);
                    if (textBlock != null)
                    {
                        string currentVal = textBlock.Text;
                        if (currentVal == "0")
                            textBlock.Text = text;
                        else
                            textBlock.Text += text;

                        // Prevent unreasonable quantities
                        if (textBlock.Text.Length > 5 ||
                            (int.TryParse(textBlock.Text, out int val) && val > 10000))
                        {
                            textBlock.Text = textBlock.Text.Substring(0, textBlock.Text.Length - text.Length);
                        }
                    }
                };
            }

            return button;
        }

        // Helper method to find a visual child of a given type
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            T childElement = null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T t)
                {
                    childElement = t;
                    break;
                }
                else
                {
                    childElement = FindVisualChild<T>(child);
                    if (childElement != null)
                        break;
                }
            }

            return childElement;
        }
    }
}