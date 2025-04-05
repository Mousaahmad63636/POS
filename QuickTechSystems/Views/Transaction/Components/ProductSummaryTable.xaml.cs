using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.WPF.Views.Dialogs;

namespace QuickTechSystems.WPF.Views.Transaction.Components
{
    public partial class ProductSummaryTable : UserControl
    {
        public ProductSummaryTable()
        {
            InitializeComponent();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void Quantity_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is TransactionDetailDTO detail)
            {
                ShowQuantityKeypad(detail);
                e.Handled = true;
            }
        }

        private void PriceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is TransactionDetailDTO detail)
            {
                ShowPriceKeypad(detail);
            }
        }

        private void ShowPriceKeypad(TransactionDetailDTO detail)
        {
            var window = new Window
            {
                Title = "Change Price",
                Width = 300,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.White
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

            // Price display
            var displayBorder = new Border
            {
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E0E0E0")),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(displayBorder, 1);

            var priceText = new TextBlock
            {
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right,
                Padding = new Thickness(10, 5, 10, 5),
                Text = detail.UnitPrice.ToString("0.00")
            };
            displayBorder.Child = priceText;
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
            AddButton(keypadGrid, ".", 3, 1);

            // Clear button
            var clearButton = AddButton(keypadGrid, "C", 3, 2);
            clearButton.Click += (s, e) => { priceText.Text = "0.00"; };

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
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F44336")),
                Foreground = System.Windows.Media.Brushes.White
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
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50")),
                Foreground = System.Windows.Media.Brushes.White
            };
            Grid.SetColumn(updateButton, 1);
            actionGrid.Children.Add(updateButton);
            updateButton.Click += (s, e) =>
            {
                if (decimal.TryParse(priceText.Text, out decimal price) && price > 0)
                {
                    // Only allow price increases
                    if (price < detail.UnitPrice)
                    {
                        MessageBox.Show(
                            "New price cannot be lower than current price.",
                            "Invalid Price",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    window.DialogResult = true;
                }
                else
                {
                    MessageBox.Show(
                        "Please enter a valid price.",
                        "Invalid Price",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            };

            window.Content = mainGrid;

            // Show dialog and process result
            if (window.ShowDialog() == true)
            {
                decimal newPrice = decimal.Parse(priceText.Text);

                // Update price in data model
                detail.UnitPrice = newPrice;
                detail.Total = detail.Quantity * newPrice;

                // Update totals in view model
                var viewModel = DataContext as TransactionViewModel;
                viewModel?.UpdateTotals();
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
                Background = System.Windows.Media.Brushes.White
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
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E0E0E0")),
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
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F44336")),
                Foreground = System.Windows.Media.Brushes.White
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
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50")),
                Foreground = System.Windows.Media.Brushes.White
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

                        // Handle decimal point for price entry
                        if (text == "." && currentVal.Contains("."))
                            return; // Prevent multiple decimal points

                        if (currentVal == "0" || currentVal == "0.00")
                        {
                            if (text == ".")
                                textBlock.Text = "0.";
                            else
                                textBlock.Text = text;
                        }
                        else
                        {
                            textBlock.Text += text;
                        }

                        // Prevent unreasonable quantities or prices
                        if (textBlock.Text.Length > 10)
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
            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

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