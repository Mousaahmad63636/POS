using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace QuickTechSystems.WPF.Views
{
    public partial class QuantityDialog : Window
    {
        public string ProductName { get; set; } = string.Empty;
        public int CurrentQuantity { get; set; }
        public int NewQuantity { get; set; }

        public QuantityDialog(string productName, int currentQuantity)
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
            e.Handled = !int.TryParse(e.Text, out _);
        }
    }
}