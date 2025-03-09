using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views.Transaction.Components
{
    public partial class ProductCard : UserControl
    {
        public ProductCard()
        {
            InitializeComponent();

            // Make the entire card clickable
            this.MouseLeftButtonDown += OnCardClicked;
            this.Cursor = Cursors.Hand;
        }

        private void OnCardClicked(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ProductDTO product)
            {
                // Show numeric keypad directly
                ShowQuantityDialog(product);
            }
        }

        private void ShowQuantityDialog(ProductDTO product)
        {
            // Create a simple dialog window
            var window = new Window
            {
                Title = "Enter Quantity",
                Width = 300,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Product name
            var productName = new TextBlock
            {
                Text = product.Name,
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
                Text = "1"
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

            // Add button
            var addButton = new Button
            {
                Content = "Add",
                Margin = new Thickness(3, 0, 3, 0),
                Padding = new Thickness(0, 10, 0, 10),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                Foreground = Brushes.White
            };
            Grid.SetColumn(addButton, 1);
            actionGrid.Children.Add(addButton);
            addButton.Click += (s, e) =>
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
                int quantity = int.Parse(quantityText.Text);

                // Use the view model to add the product with quantity
                var viewModel = FindViewModel();
                if (viewModel != null)
                {
                    // Add to cart with quantity
                    viewModel.AddProductToTransaction(product, quantity);
                }
            }

            // Helper method to add numbered buttons
            Button AddButton(Grid grid, string text, int row, int col)
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
                        string currentVal = quantityText.Text;
                        if (currentVal == "0")
                            quantityText.Text = text;
                        else
                            quantityText.Text += text;

                        // Prevent unreasonable quantities
                        if (quantityText.Text.Length > 5 ||
                            (int.TryParse(quantityText.Text, out int val) && val > 10000))
                        {
                            quantityText.Text = quantityText.Text.Substring(0, quantityText.Text.Length - text.Length);
                        }
                    };
                }

                return button;
            }
        }

        private TransactionViewModel FindViewModel()
        {
            DependencyObject current = this;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.DataContext is TransactionViewModel vm)
                {
                    return vm;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void Cleanup()
        {
            this.MouseLeftButtonDown -= OnCardClicked;
        }

        ~ProductCard()
        {
            Cleanup();
        }
    }
}