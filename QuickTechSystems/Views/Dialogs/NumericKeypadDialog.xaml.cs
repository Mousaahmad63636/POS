using System;
using System.Windows;
using System.ComponentModel;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Views.Dialogs
{
    public partial class NumericKeypadDialog : Window, INotifyPropertyChanged
    {
        private string _quantityStr = "1";

        public event PropertyChangedEventHandler PropertyChanged;

        public string QuantityStr
        {
            get => _quantityStr;
            set
            {
                if (_quantityStr != value)
                {
                    _quantityStr = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QuantityStr)));
                }
            }
        }

        public int Quantity
        {
            get
            {
                if (int.TryParse(_quantityStr, out int qty) && qty > 0)
                    return qty;
                return 1;
            }
        }

        public string ProductName { get; private set; }
        public ProductDTO SelectedProduct { get; private set; }

        public NumericKeypadDialog(ProductDTO product)
        {
            InitializeComponent();

            SelectedProduct = product;
            ProductName = product.Name;

            DataContext = this;

            // Focus on the text box when the dialog is shown
            Loaded += (s, e) =>
            {
                DisplayText.Focus();
                DisplayText.SelectAll();
            };
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