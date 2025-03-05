using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.Services;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel
    {
        private CustomerDTO _newCustomer;

        public CustomerDTO NewCustomer
        {
            get => _newCustomer;
            set => SetProperty(ref _newCustomer, value);
        }
        private async Task CheckPrice()
        {
            try
            {
                // Create the dialog without setting owner initially
                var dialog = new InputDialog("Price Check", "Enter product barcode or code");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    // Show the dialog on the UI thread
                    if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Input))
                    {
                        var product = await _productService.GetByBarcodeAsync(dialog.Input);
                        if (product != null)
                        {
                            var message = $"Product: {product.Name}\n" +
                                        $"Price: {product.SalePrice:C}\n" +
                                        $"Current Stock: {product.CurrentStock}\n" +
                                        $"Category: {product.CategoryName}";

                            MessageBox.Show(message, "Price Check",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("Product not found", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error checking price: {ex.Message}");
            }
        }
        private Window GetMainWindow()
        {
            return System.Windows.Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current.MainWindow;
        }
        private void ClearCustomer()
        {
            SelectedCustomer = null;
            CustomerSearchText = string.Empty;
            IsCustomerSearchVisible = false;
        }
        private async void SearchCustomers()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(CustomerSearchText))
                {
                    FilteredCustomers.Clear();
                    IsCustomerSearchVisible = false;
                    return;
                }

                var customers = await _customerService.GetByNameAsync(CustomerSearchText);

                // Update the UI on the dispatcher thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FilteredCustomers = new ObservableCollection<CustomerDTO>(customers);
                    IsCustomerSearchVisible = FilteredCustomers.Any();
                });
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error searching customers: {ex.Message}");
            }
        }

        private async void SearchProducts()
        {
            if (string.IsNullOrWhiteSpace(ProductSearchText))
            {
                FilteredProducts.Clear();
                IsProductSearchVisible = false;
                return;
            }

            try
            {
                var products = await _productService.GetAllAsync();
                var filtered = products.Where(p =>
                    p.Name.Contains(ProductSearchText, StringComparison.OrdinalIgnoreCase) ||
                    p.CategoryName.Contains(ProductSearchText, StringComparison.OrdinalIgnoreCase))
                    .Take(10)  // Limit results for better performance
                    .ToList();

                FilteredProducts = new ObservableCollection<ProductDTO>(filtered);
                IsProductSearchVisible = FilteredProducts.Any();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error searching products: {ex.Message}");
            }
        }

        public void OnProductSelected(ProductDTO product)
        {
            if (product != null)
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AddProductToTransaction(product);
                    ProductSearchText = string.Empty;
                    IsProductSearchVisible = false;
                    OnPropertyChanged(nameof(CurrentTransaction));
                });
            }
        }

        public void CloseProductSearch()
        {
            IsProductSearchVisible = false;
            ProductSearchText = string.Empty;
        }

        private async Task ShowNewCustomerDialog()
        {
            try
            {
                // Initialize new customer
                NewCustomer = new CustomerDTO
                {
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                // Get the global overlay service
                var overlayService = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<IGlobalOverlayService>();

                // Show the customer editor form
                overlayService.ShowCustomerEditor(this);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error showing customer form: {ex.Message}");
            }
        }
        private void CancelNewCustomer()
        {
            var overlayService = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<IGlobalOverlayService>();
            overlayService.HideCustomerEditor();
            NewCustomer = null;
        }
        private async Task SaveNewCustomerAsync()
        {
            try
            {
                if (NewCustomer == null)
                    return;

                // Validate required fields
                if (string.IsNullOrWhiteSpace(NewCustomer.Name))
                {
                    await ShowErrorMessageAsync("Customer name is required");
                    return;
                }

                // Save the customer
                var createdCustomer = await _customerService.CreateAsync(NewCustomer);

                if (createdCustomer != null)
                {
                    // Set as selected customer in the transaction
                    SelectedCustomer = createdCustomer;

                    // Update the customer search text
                    CustomerSearchText = createdCustomer.Name;

                    // Hide the form
                    var overlayService = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<IGlobalOverlayService>();
                    overlayService.HideCustomerEditor();

                    // Show success message
                    await ShowSuccessMessage("Customer created successfully");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error creating customer: {ex.Message}");
            }
        }
        private async Task ChangeItemQuantityAsync()
        {
            try
            {
                // Check if we have any transaction or details at all
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    await ShowErrorMessageAsync("No items in transaction");
                    return;
                }

                // Get the item to modify (either selected or first)
                var selectedItems = CurrentTransaction.Details.Where(d => d.IsSelected).ToList();
                var itemToModify = selectedItems.FirstOrDefault() ?? CurrentTransaction.Details.First();

                // Create and show the quantity dialog
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var mainWindow = System.Windows.Application.Current.MainWindow;
                        var dialog = new QuantityDialog(itemToModify.ProductName, itemToModify.Quantity)
                        {
                            Owner = mainWindow,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            // Update the quantity and total
                            itemToModify.Quantity = dialog.NewQuantity;
                            itemToModify.Total = itemToModify.Quantity * itemToModify.UnitPrice;

                            // Force UI refresh
                            var index = CurrentTransaction.Details.IndexOf(itemToModify);
                            if (index >= 0)
                            {
                                CurrentTransaction.Details.RemoveAt(index);
                                CurrentTransaction.Details.Insert(index, itemToModify);
                            }

                            // Update totals
                            UpdateTotals();
                            OnPropertyChanged(nameof(CurrentTransaction.Details));
                            OnPropertyChanged(nameof(CurrentTransaction));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating quantity: {ex.Message}");
                        MessageBox.Show("Error updating quantity", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ChangeItemQuantityAsync: {ex.Message}");
                await ShowErrorMessageAsync("Error changing quantity");
            }
        }
    }
}