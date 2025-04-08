using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QuickTechSystems.WPF.Views
{
    public partial class QuantityDialog : Window
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal CurrentQuantity { get; set; }
        public decimal NewQuantity { get; set; }

        public QuantityDialog(string productName, decimal currentQuantity)
        {
            InitializeComponent();
            ProductName = productName;
            CurrentQuantity = currentQuantity;
            NewQuantity = currentQuantity;
            DataContext = this;

            // Handle closing
            this.Closing += (s, e) =>
            {
                if (!DialogResult.HasValue)
                    DialogResult = false;
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (NewQuantity <= 0)
                {
                    MessageBox.Show(this, "Quantity must be greater than zero", "Invalid Quantity",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Get the text that would result if the input is accepted
                string proposedText = textBox.Text.Substring(0, textBox.SelectionStart) +
                                     e.Text +
                                     textBox.Text.Substring(textBox.SelectionStart + textBox.SelectionLength);

                // Check if the proposed text is a valid decimal
                e.Handled = !decimal.TryParse(proposedText, NumberStyles.AllowDecimalPoint,
                                            CultureInfo.InvariantCulture, out _);
            }
            else
            {
                // Fallback validation if sender is not a TextBox
                e.Handled = !decimal.TryParse(e.Text, NumberStyles.AllowDecimalPoint,
                                            CultureInfo.InvariantCulture, out _);
            }
        }
    }
}