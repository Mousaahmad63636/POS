using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickTechSystems.WPF.Views
{
    public partial class PriceDialog : Window
    {
        public decimal NewPrice { get; private set; }
        private decimal _originalPrice;

        public PriceDialog(string productName, decimal currentPrice)
        {
            InitializeComponent();

            ProductNameText.Text = productName;
            _originalPrice = currentPrice;
            PriceText.Text = currentPrice.ToString("0.00");

            InitializeKeypad();
        }

        private void InitializeKeypad()
        {
            // Add number buttons
            AddButton("7", 0, 0);
            AddButton("8", 0, 1);
            AddButton("9", 0, 2);
            AddButton("4", 1, 0);
            AddButton("5", 1, 1);
            AddButton("6", 1, 2);
            AddButton("1", 2, 0);
            AddButton("2", 2, 1);
            AddButton("3", 2, 2);
            AddButton("0", 3, 0);
            AddButton(".", 3, 1);

            // Clear button
            var clearButton = AddButton("C", 3, 2);
            clearButton.Click += (s, e) => { PriceText.Text = "0.00"; };
        }

        private Button AddButton(string text, int row, int col)
        {
            var button = new Button
            {
                Content = text,
                FontSize = 20,
                Margin = new Thickness(3)
            };
            Grid.SetRow(button, row);
            Grid.SetColumn(button, col);
            KeypadGrid.Children.Add(button);

            // Add click handler for number buttons
            if (text != "C")
            {
                button.Click += (s, e) =>
                {
                    if (PriceText.Text == "0.00" || PriceText.Text == "0")
                    {
                        if (text == ".")
                            PriceText.Text = "0.";
                        else
                            PriceText.Text = text;
                    }
                    else
                    {
                        // Handle decimal point
                        if (text == "." && PriceText.Text.Contains("."))
                            return;

                        PriceText.Text += text;
                    }
                };
            }

            return button;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(PriceText.Text, out decimal price))
            {
                // Validate price is higher than original
                if (price < _originalPrice)
                {
                    MessageBox.Show(
                        "New price cannot be lower than current price.",
                        "Invalid Price",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                NewPrice = price;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(
                    "Please enter a valid price.",
                    "Invalid Price",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}