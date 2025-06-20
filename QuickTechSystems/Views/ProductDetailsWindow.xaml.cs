using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Diagnostics;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class ProductDetailsWindow : Window
    {
        public event RoutedEventHandler SaveCompleted;
        private static readonly Regex _decimalRegex = new Regex(@"^[0-9]*(?:\.[0-9]*)?$");
        private static readonly Regex _integerRegex = new Regex(@"^[0-9]*$");

        public ProductDetailsWindow()
        {
            InitializeComponent();
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCompleted?.Invoke(this, new RoutedEventArgs());
            this.DialogResult = true;
            this.Close();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void PriceTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                string rawValue = textBox.Text.Replace("$", "").Replace(",", "").Trim();

                if (decimal.TryParse(rawValue, out decimal value) && value == 0)
                {
                    textBox.Text = string.Empty;
                }
            }
        }

        private void PriceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    textBox.Text = "0";
                }
                else if (decimal.TryParse(textBox.Text, out decimal value))
                {
                    textBox.Text = value.ToString("0.00").Replace(",", ".");
                }
            }
        }

        private void DecimalTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            var newText = textBox.Text.Insert(textBox.CaretIndex, e.Text);
            e.Handled = !_decimalRegex.IsMatch(newText);
        }

        private void IntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            var newText = textBox.Text.Insert(textBox.CaretIndex, e.Text);
            e.Handled = !_integerRegex.IsMatch(newText);
        }

        private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                var textBox = sender as TextBox;
                var isInteger = (string)textBox.Tag == "integer";
                var regex = isInteger ? _integerRegex : _decimalRegex;

                if (!regex.IsMatch(text))
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