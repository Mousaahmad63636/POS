// NumericKeypadDialog.xaml.cs
using System;
using System.Windows;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Views.Dialogs
{
    public partial class NumericKeypadDialog : Window
    {
        private string _quantityStr = "1";

        public int Quantity
        {
            get { return int.Parse(_quantityStr); }
        }

        public string ProductName { get; private set; }
        public ProductDTO SelectedProduct { get; private set; }

        public NumericKeypadDialog(ProductDTO product)
        {
            InitializeComponent();

            SelectedProduct = product;
            ProductName = product.Name;

            DisplayText.Text = _quantityStr;
            DataContext = this;
        }

        private void NumberButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string digit = button.Content.ToString();

                // If the current value is just "0", replace it
                if (_quantityStr == "0")
                {
                    _quantityStr = digit;
                }
                else
                {
                    // Otherwise append the digit
                    _quantityStr += digit;
                }

                // Prevent unreasonable quantities
                if (_quantityStr.Length > 5 ||
                    (int.TryParse(_quantityStr, out int qty) && qty > 10000))
                {
                    _quantityStr = _quantityStr.Substring(0, _quantityStr.Length - digit.Length);
                }

                DisplayText.Text = _quantityStr;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _quantityStr = "0";
            DisplayText.Text = _quantityStr;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // Make sure we have at least 1 quantity
            if (int.TryParse(_quantityStr, out int qty) && qty > 0)
            {
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please enter a valid quantity.", "Invalid Quantity",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}