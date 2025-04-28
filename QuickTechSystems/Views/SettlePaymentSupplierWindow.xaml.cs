using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using System.Collections.ObjectModel;

namespace QuickTechSystems.WPF.Views
{
    public partial class SettlePaymentSupplierWindow : Window
    {
        private readonly SupplierInvoiceDTO _invoice;
        private readonly ISupplierInvoiceService _supplierInvoiceService;
        private ObservableCollection<SupplierTransactionDTO> _invoicePayments;
        private decimal _paymentAmount;
        private decimal _remainingAmount;

        public SupplierInvoiceDTO SelectedInvoice => _invoice;
        public ObservableCollection<SupplierTransactionDTO> InvoicePayments => _invoicePayments;

        public decimal PaymentAmount
        {
            get => _paymentAmount;
            set
            {
                _paymentAmount = value;
                // Add validation logic if needed
            }
        }

        public decimal RemainingAmount => _remainingAmount;

        public SettlePaymentSupplierWindow(SupplierInvoiceDTO invoice,
                                        ISupplierInvoiceService supplierInvoiceService,
                                        ObservableCollection<SupplierTransactionDTO> payments)
        {
            InitializeComponent();
            DataContext = this;

            _invoice = invoice;
            _supplierInvoiceService = supplierInvoiceService;
            _invoicePayments = payments;

            // Calculate remaining amount
            decimal totalPaid = payments?.Sum(p => Math.Abs(p.Amount)) ?? 0;
            _remainingAmount = invoice.TotalAmount - totalPaid;

            // Default to paying the full remaining amount
            _paymentAmount = _remainingAmount;
        }

        private void PaymentAmountTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            // Focus and select all text when loaded
            if (sender is TextBox textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                ProcessPayment();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void ProcessPayment_Click(object sender, RoutedEventArgs e)
        {
            ProcessPayment();
        }

        private async void ProcessPayment()
        {
            try
            {
                // Validate payment amount
                if (PaymentAmount <= 0)
                {
                    MessageBox.Show("Payment amount must be greater than zero.", "Invalid Amount",
                                   MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (PaymentAmount > RemainingAmount)
                {
                    MessageBox.Show($"Payment amount cannot exceed the remaining amount ({RemainingAmount:C}).",
                                   "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Process the payment
                bool success = await _supplierInvoiceService.SettleInvoiceAsync(SelectedInvoice.SupplierInvoiceId, PaymentAmount);

                if (success)
                {
                    MessageBox.Show("Payment processed successfully.", "Success",
                                   MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Failed to process payment. Please try again.", "Error",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing payment: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}