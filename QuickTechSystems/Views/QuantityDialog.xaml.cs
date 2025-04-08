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

            // Set focus to the quantity textbox and select all text when the dialog loads
            this.Loaded += (s, e) =>
            {
                QuantityTextBox.Focus();
                QuantityTextBox.SelectAll();
            };

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
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // Get the text that would result after this input
            string proposedText = textBox.Text.Substring(0, textBox.SelectionStart) +
                                 e.Text +
                                 textBox.Text.Substring(textBox.SelectionStart + textBox.SelectionLength);

            // Special handling for decimal separator (both period and comma)
            if (e.Text == "." || e.Text == ",")
            {
                // Only allow one decimal separator
                if (textBox.Text.Contains(".") || textBox.Text.Contains(","))
                {
                    e.Handled = true;
                    return;
                }

                // Allow the decimal separator
                e.Handled = false;
                return;
            }

            // For other characters, check if the result would be a valid decimal
            e.Handled = !decimal.TryParse(proposedText,
                                     NumberStyles.AllowDecimalPoint,
                                     CultureInfo.InvariantCulture,
                                     out _);
        }

        private void QuantityTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Allow navigation keys
            if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Left ||
                e.Key == Key.Right || e.Key == Key.Tab || e.Key == Key.Enter)
                return;

            // Allow digits on the number pad
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                return;

            // Allow regular digits
            if (e.Key >= Key.D0 && e.Key <= Key.D9 && e.KeyboardDevice.Modifiers != ModifierKeys.Shift)
                return;

            // Allow decimal point (both period and decimal key)
            if (e.Key == Key.Decimal || e.Key == Key.OemPeriod || e.Key == Key.OemComma)
            {
                var textBox = sender as TextBox;
                if (textBox != null && !textBox.Text.Contains(".") && !textBox.Text.Contains(","))
                    return;
            }

            // Block all other keys
            e.Handled = true;
        }

        private void QuantityTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string pastedText = (string)e.DataObject.GetData(typeof(string));

                var textBox = sender as TextBox;
                if (textBox != null)
                {
                    // Construct the text that would result after pasting
                    string proposedText = textBox.Text.Substring(0, textBox.SelectionStart) +
                                         pastedText +
                                         textBox.Text.Substring(textBox.SelectionStart + textBox.SelectionLength);

                    // Check if the result would be a valid decimal
                    if (!decimal.TryParse(proposedText,
                                       NumberStyles.AllowDecimalPoint,
                                       CultureInfo.InvariantCulture,
                                       out _))
                    {
                        e.CancelCommand();
                    }
                }
                else
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
    }
}