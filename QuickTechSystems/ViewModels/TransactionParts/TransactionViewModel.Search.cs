using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel
    {
        private async Task CheckPrice()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                // Create the dialog without setting owner initially
                var dialog = new InputDialog("Price Check", "Enter product barcode or code");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in price check: {ex.Message}");
                        MessageBox.Show($"Error checking price: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            }, "Checking product price", "ProductSearch");
        }

        private Window GetMainWindow()
        {
            return System.Windows.Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current.MainWindow;
        }

        private CancellationTokenSource _customerSearchCts = new CancellationTokenSource();
        private static SemaphoreSlim _customerSearchSemaphore = new SemaphoreSlim(1, 1);

        private async void SearchCustomers()
        {
            // Exit immediately if dropdown suppression is active
            if (_suppressCustomerDropdown)
            {
                Debug.WriteLine("Customer search suppressed - exiting early");
                return;
            }

            try
            {
                // Cancel any previous search
                _customerSearchCts.Cancel();
                _customerSearchCts = new CancellationTokenSource();
                var token = _customerSearchCts.Token;

                if (string.IsNullOrWhiteSpace(CustomerSearchText))
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        FilteredCustomers.Clear();
                        IsCustomerSearchVisible = false;
                    });
                    return;
                }

                // Add a slight delay to avoid running searches for every keystroke
                await Task.Delay(300, token);

                // Check suppression flag again after delay
                if (_suppressCustomerDropdown || token.IsCancellationRequested)
                {
                    Debug.WriteLine("Search canceled after delay (suppressed or token canceled)");
                    return;
                }

                // Continue with search only if we're not suppressing and not canceled
                await ExecuteOperationSafelyAsync(async () =>
                {
                    // Additional check in case flag changed during execution
                    if (_suppressCustomerDropdown) return;

                    string searchText = CustomerSearchText;
                    if (string.IsNullOrWhiteSpace(searchText)) return;

                    // Check if search exactly matches selected customer (prevent dropdown showing)
                    if (SelectedCustomer != null &&
                        SelectedCustomer.Name.Equals(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine("Search matches selected customer - not showing dropdown");
                        return;
                    }

                    var customers = await Task.Run(async () =>
                        await _customerService.GetByNameAsync(searchText), token);

                    if (token.IsCancellationRequested) return;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        FilteredCustomers = new ObservableCollection<CustomerDTO>(customers);

                        // Only show if we have results AND not suppressing AND text doesn't match selection
                        bool shouldShowDropdown = FilteredCustomers.Any() &&
                                                  !_suppressCustomerDropdown &&
                                                  !(SelectedCustomer != null &&
                                                    SelectedCustomer.Name.Equals(searchText,
                                                                                StringComparison.OrdinalIgnoreCase));

                        IsCustomerSearchVisible = shouldShowDropdown;

                        OnPropertyChanged(nameof(FilteredCustomers));
                        OnPropertyChanged(nameof(IsCustomerSearchVisible));
                    });
                }, "Searching customers", "CustomerSearch");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Customer search operation was canceled");
            }
            catch (Exception ex)
            {
                if (ex is TaskCanceledException) return;
                Debug.WriteLine($"Error in SearchCustomers: {ex.Message}");
                await ShowErrorMessageAsync($"Error searching customers: {ex.Message}");
            }
        }

        private async void SearchProducts()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(ProductSearchText))
                {
                    FilteredProducts.Clear();
                    IsProductSearchVisible = false;
                    return;
                }

                var products = await _productService.GetAllAsync();
                var filtered = products
                    .Where(p => p.IsActive) // Filter for active products only
                    .Where(p =>
                        p.Name.Contains(ProductSearchText, StringComparison.OrdinalIgnoreCase) ||
                        p.CategoryName.Contains(ProductSearchText, StringComparison.OrdinalIgnoreCase))
                    .Take(10)  // Limit results for better performance
                    .ToList();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FilteredProducts = new ObservableCollection<ProductDTO>(filtered);
                    IsProductSearchVisible = FilteredProducts.Any();
                });
            }, "Searching products", "ProductSearch");
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
            await ExecuteOperationSafelyAsync(async () =>
            {
                var newCustomer = new CustomerDTO
                {
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                var nameDialog = new InputDialog("New Customer", "Enter customer name");
                var nameResult = await ShowDialog(nameDialog);

                if (nameResult == true)
                {
                    newCustomer.Name = nameDialog.Input;

                    var phoneDialog = new InputDialog("New Customer", "Enter customer phone");
                    var phoneResult = await ShowDialog(phoneDialog);

                    if (phoneResult == true)
                    {
                        newCustomer.Phone = phoneDialog.Input;

                        var emailDialog = new InputDialog("New Customer", "Enter customer email");
                        var emailResult = await ShowDialog(emailDialog);

                        if (emailResult == true)
                        {
                            newCustomer.Email = emailDialog.Input;
                        }

                        if (!string.IsNullOrWhiteSpace(newCustomer.Name))
                        {
                            var createdCustomer = await _customerService.CreateAsync(newCustomer);
                            if (createdCustomer != null)
                            {
                                SelectedCustomer = createdCustomer;
                                await ShowSuccessMessage("Customer created successfully");
                            }
                        }
                    }
                }
            }, "Creating new customer");
        }

        private async Task ChangeItemQuantityAsync()
        {
            // Preliminary check before entering the safe operation
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                await ShowErrorMessageAsync("No items in transaction to change quantity");
                return;
            }

            await ExecuteOperationSafelyAsync(async () =>
            {
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
            }, "Changing item quantity");
        }
    }
}