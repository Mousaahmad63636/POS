// Path: QuickTechSystems.WPF.Views/QuickSupplierInvoiceDialog.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;

namespace QuickTechSystems.WPF.Views
{
    public partial class QuickSupplierInvoiceDialog : Window
    {
        private ISupplierService _supplierService;
        private ISupplierInvoiceService _supplierInvoiceService;

        public ObservableCollection<SupplierDTO> Suppliers { get; set; }
        public DateTime CurrentDate { get; set; } = DateTime.Now;
        public SupplierInvoiceDTO CreatedInvoice { get; private set; }

        public QuickSupplierInvoiceDialog()
        {
            InitializeComponent();

            // Get services from application
            _supplierService = ((App)System.Windows.Application.Current).ServiceProvider.GetService(typeof(ISupplierService)) as ISupplierService;
            _supplierInvoiceService = ((App)System.Windows.Application.Current).ServiceProvider.GetService(typeof(ISupplierInvoiceService)) as ISupplierInvoiceService;

            // Load suppliers
            LoadSuppliersAsync();
            DataContext = this;
        }

        private async void LoadSuppliersAsync()
        {
            try
            {
                var suppliers = await _supplierService.GetActiveAsync();
                Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                DataContext = null;
                DataContext = this;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading suppliers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HeaderPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (SupplierComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select a supplier.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(InvoiceNumberTextBox.Text))
                {
                    MessageBox.Show("Please enter an invoice number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!decimal.TryParse(TotalAmountTextBox.Text, NumberStyles.Currency, CultureInfo.CurrentCulture, out decimal totalAmount) || totalAmount <= 0)
                {
                    MessageBox.Show("Please enter a valid total amount greater than zero.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selectedSupplier = SupplierComboBox.SelectedItem as SupplierDTO;

                // Create the invoice
                var invoice = new SupplierInvoiceDTO
                {
                    SupplierId = selectedSupplier.SupplierId,
                    SupplierName = selectedSupplier.Name,
                    InvoiceNumber = InvoiceNumberTextBox.Text.Trim(),
                    InvoiceDate = InvoiceDatePicker.SelectedDate ?? DateTime.Now,
                    TotalAmount = totalAmount,
                    Status = "Draft",
                    Notes = NotesTextBox.Text,
                    CreatedAt = DateTime.Now
                };

                // Check if invoice with same number already exists
                var existingInvoice = await _supplierInvoiceService.GetByInvoiceNumberAsync(invoice.InvoiceNumber, invoice.SupplierId);
                if (existingInvoice != null)
                {
                    MessageBox.Show($"An invoice with number '{invoice.InvoiceNumber}' already exists for this supplier.",
                        "Duplicate Invoice", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Save the invoice
                CreatedInvoice = await _supplierInvoiceService.CreateAsync(invoice);

                MessageBox.Show($"Invoice '{CreatedInvoice.InvoiceNumber}' created successfully.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating invoice: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddSupplierButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new QuickSupplierDialogWindow
                {
                    Owner = this
                };

                var result = dialog.ShowDialog();
                if (result == true && dialog.NewSupplier != null)
                {
                    var newSupplier = await _supplierService.CreateAsync(dialog.NewSupplier);

                    Suppliers.Add(newSupplier);
                    SupplierComboBox.SelectedItem = newSupplier;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding supplier: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}