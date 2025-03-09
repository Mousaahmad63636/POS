using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.WPF.Views.Dialogs;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionView : UserControl
    {
        public TransactionView()
        {
            InitializeComponent();
            SetupKeyboardShortcuts();
            this.Loaded += TransactionView_Loaded;
            this.SizeChanged += OnControlSizeChanged;
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

            // Get the main grid (the root element of the UserControl)
            // Since we can't use MainGrid, we'll get the first child of the UserControl
            if (!(this.Content is Grid mainGrid)) return;

            // Main content grid (the one containing the left and right panels)
            if (mainGrid.Children.Count < 2 || !(mainGrid.Children[1] is Grid mainContentGrid)) return;

            // Left panel (transaction details)
            if (mainContentGrid.Children.Count < 1 || !(mainContentGrid.Children[0] is Border leftPanel)) return;

            // Right panel (payment area)
            if (mainContentGrid.Children.Count < 2 || !(mainContentGrid.Children[1] is Border rightPanel)) return;

            // Adjust margins based on window size
            if (windowWidth >= 1920) // Large screens
            {
                leftPanel.Margin = new Thickness(20, 20, 10, 20);
                rightPanel.Margin = new Thickness(10, 20, 20, 20);

                // Only adjust column width if there are column definitions
                if (mainContentGrid.ColumnDefinitions.Count >= 2)
                    mainContentGrid.ColumnDefinitions[1].Width = new GridLength(450);
            }
            else if (windowWidth >= 1366) // Medium screens
            {
                leftPanel.Margin = new Thickness(16, 16, 8, 16);
                rightPanel.Margin = new Thickness(8, 16, 16, 16);

                if (mainContentGrid.ColumnDefinitions.Count >= 2)
                    mainContentGrid.ColumnDefinitions[1].Width = new GridLength(400);
            }
            else if (windowWidth >= 800) // Small screens
            {
                leftPanel.Margin = new Thickness(12, 12, 6, 12);
                rightPanel.Margin = new Thickness(6, 12, 12, 12);

                if (mainContentGrid.ColumnDefinitions.Count >= 2)
                    mainContentGrid.ColumnDefinitions[1].Width = new GridLength(350);
            }
            else // Very small screens
            {
                leftPanel.Margin = new Thickness(8, 8, 4, 8);
                rightPanel.Margin = new Thickness(4, 8, 8, 8);

                if (mainContentGrid.ColumnDefinitions.Count >= 2)
                    mainContentGrid.ColumnDefinitions[1].Width = new GridLength(300);
            }

            // Adjust inner content padding as needed
            if (leftPanel.Child is Grid leftContent)
            {
                if (windowWidth >= 1366)
                    leftContent.Margin = new Thickness(20);
                else
                    leftContent.Margin = new Thickness(12);
            }

            if (rightPanel.Child is Grid rightContent)
            {
                if (windowWidth >= 1366)
                    rightContent.Margin = new Thickness(20);
                else
                    rightContent.Margin = new Thickness(12);
            }
        }

        private void SetupKeyboardShortcuts()
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.KeyDown += (s, e) =>
                {
                    var vm = DataContext as TransactionViewModel;
                    if (vm == null) return;

                    switch (e.Key)
                    {
                        case Key.F2: vm.VoidLastItemCommand.Execute(null); break;
                        case Key.F3: vm.ChangeQuantityCommand.Execute(null); break;
                        case Key.F4: vm.PriceCheckCommand.Execute(null); break;
                        case Key.F5: vm.AddDiscountCommand.Execute(null); break;
                        case Key.F6: vm.HoldTransactionCommand.Execute(null); break;
                        case Key.F7: vm.RecallTransactionCommand.Execute(null); break;
                        case Key.F8: vm.ProcessReturnCommand.Execute(null); break;
                        case Key.F9: vm.ReprintLastCommand.Execute(null); break;
                        case Key.F10: vm.ClearTransactionCommand.Execute(null); break;
                        case Key.F12: vm.CashPaymentCommand.Execute(null); break;
                        case Key.Escape: vm.CancelTransactionCommand.Execute(null); break;
                        case Key.V:
                            if (Keyboard.Modifiers == ModifierKeys.Control)
                            {
                                vm.ToggleViewCommand.Execute(null);
                            }
                            break;
                    }
                };
            }
        }

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