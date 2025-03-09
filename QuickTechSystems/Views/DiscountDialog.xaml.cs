using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace QuickTechSystems.WPF.Views
{
    public partial class DiscountDialog : Window, INotifyPropertyChanged
    {
        private decimal _currentTotal;
        private int _discountType;
        private decimal _discountValue;
        private decimal _finalAmount;

        public event PropertyChangedEventHandler? PropertyChanged;

        public decimal CurrentTotal
        {
            get => _currentTotal;
            set
            {
                _currentTotal = value;
                OnPropertyChanged(nameof(CurrentTotal));
                CalculateFinalAmount();
            }
        }

        public int DiscountType
        {
            get => _discountType;
            set
            {
                _discountType = value;
                OnPropertyChanged(nameof(DiscountType));
                CalculateFinalAmount();
            }
        }

        public decimal DiscountValue
        {
            get => _discountValue;
            set
            {
                _discountValue = value;
                OnPropertyChanged(nameof(DiscountValue));
                CalculateFinalAmount();
            }
        }

        public decimal FinalAmount
        {
            get => _finalAmount;
            set
            {
                _finalAmount = value;
                OnPropertyChanged(nameof(FinalAmount));
            }
        }

        public decimal DiscountAmount { get; private set; }

        public DiscountDialog(decimal currentTotal)
        {
            InitializeComponent();
            CurrentTotal = currentTotal;
            DataContext = this;
        }

        private void CalculateFinalAmount()
        {
            try
            {
                if (DiscountType == 0) // Percentage
                {
                    DiscountAmount = CurrentTotal * (DiscountValue / 100m);
                }
                else // Amount
                {
                    DiscountAmount = DiscountValue;
                }

                FinalAmount = CurrentTotal - DiscountAmount;
            }
            catch
            {
                FinalAmount = CurrentTotal;
                DiscountAmount = 0;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (DiscountAmount > CurrentTotal)
            {
                MessageBox.Show("Discount cannot be greater than total amount.",
                    "Invalid Discount", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"^[0-9]*\.?[0-9]*$");
            e.Handled = !regex.IsMatch(e.Text);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}