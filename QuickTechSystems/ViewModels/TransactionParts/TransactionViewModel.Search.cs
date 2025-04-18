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
                        // Get a valid owner window
                        dialog.Owner = GetOwnerWindow() ?? System.Windows.Application.Current.MainWindow;
                        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                        // Show the dialog on the UI thread
                        if (dialog.ShowDialog() == true)
                        {
                            if (string.IsNullOrWhiteSpace(dialog.Input))
                            {
                                MessageBox.Show("Please enter a valid barcode or product code.",
                                    "Empty Input",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                                return;
                            }

                            // Search for the product
                            StatusMessage = $"Checking price for: {dialog.Input}";
                            OnPropertyChanged(nameof(StatusMessage));

                            var product = await _productService.GetByBarcodeAsync(dialog.Input);

                            if (product != null)
                            {
                                string activeStatus = product.IsActive ? "Active" : "Inactive";
                                var message = $"Product: {product.Name}\n" +
                                            $"Price: {product.SalePrice:C}\n" +
                                            $"Current Stock: {product.CurrentStock}\n" +
                                            $"Category: {product.CategoryName}\n" +
                                            $"Status: {activeStatus}";

                                MessageBox.Show(message, "Price Check",
                                    MessageBoxButton.OK, MessageBoxImage.Information);

                                StatusMessage = "Price check completed";
                            }
                            else
                            {
                                MessageBox.Show("This barcode is not registered. Please check.",
                                    "Unknown Barcode",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);

                                StatusMessage = "Unknown barcode";
                            }

                            OnPropertyChanged(nameof(StatusMessage));
                        }
                        else
                        {
                            StatusMessage = "Price check cancelled";
                            OnPropertyChanged(nameof(StatusMessage));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in price check: {ex.Message}");
                        MessageBox.Show("An unexpected error occurred. Please try again.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);

                        StatusMessage = "Error checking price";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                });
            }, "Checking product price", "ProductSearch");
        }

        private Window GetMainWindow()
        {
            try
            {
                return System.Windows.Application.Current.Windows.OfType<Window>()
                    .FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current.MainWindow;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting main window: {ex.Message}");
                return null;
            }
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

            // Clear any error indicators
            IsSearchMessageVisible = false;
            OnPropertyChanged(nameof(IsSearchMessageVisible));

            try
            {
                // Cancel any previous search
                try
                {
                    _customerSearchCts.Cancel();
                    _customerSearchCts.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cancelling previous search: {ex.Message}");
                }

                // Create new cancellation token source
                _customerSearchCts = new CancellationTokenSource();
                var token = _customerSearchCts.Token;

                if (string.IsNullOrWhiteSpace(CustomerSearchText))
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            FilteredCustomers.Clear();
                            IsCustomerSearchVisible = false;
                            OnPropertyChanged(nameof(FilteredCustomers));
                            OnPropertyChanged(nameof(IsCustomerSearchVisible));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error clearing customer results: {ex.Message}");
                        }
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

                // Show loading indicator
                IsSearching = true;
                OnPropertyChanged(nameof(IsSearching));

                // Continue with search only if we're not suppressing and not canceled
                await ExecuteOperationSafelyAsync(async () =>
                {
                    // Additional check in case flag changed during execution
                    if (_suppressCustomerDropdown) return;

                    string searchText = CustomerSearchText;
                    if (string.IsNullOrWhiteSpace(searchText)) return;

                    // Check if search exactly matches selected customer (prevent dropdown showing)
                    if (SelectedCustomer != null &&
                        SelectedCustomer.Name?.Equals(searchText, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        Debug.WriteLine("Search matches selected customer - not showing dropdown");
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                FilteredCustomers.Clear();
                                IsCustomerSearchVisible = false;
                                OnPropertyChanged(nameof(FilteredCustomers));
                                OnPropertyChanged(nameof(IsCustomerSearchVisible));
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error updating UI: {ex.Message}");
                            }
                        });
                        return;
                    }

                    // Use a try-catch block to handle exceptions during the search
                    try
                    {
                        var customers = await Task.Run(async () =>
                            await _customerService.GetByNameAsync(searchText), token);

                        if (token.IsCancellationRequested) return;

                        // Keep track of processed customer IDs to avoid duplicates
                        HashSet<int> processedIds = new HashSet<int>();

                        // Create a new collection with only unique customers
                        var uniqueCustomers = new List<CustomerDTO>();

                        if (customers != null)
                        {
                            foreach (var customer in customers)
                            {
                                if (customer == null) continue;

                                // Only add if we haven't seen this ID before
                                if (!processedIds.Contains(customer.CustomerId))
                                {
                                    // Create a new customer object without balance information
                                    var sanitizedCustomer = new CustomerDTO
                                    {
                                        CustomerId = customer.CustomerId,
                                        Name = customer.Name ?? "Unknown",
                                        Phone = customer.Phone ?? string.Empty,
                                        Email = customer.Email ?? string.Empty,
                                        Address = customer.Address ?? string.Empty,
                                        IsActive = customer.IsActive,
                                        CreatedAt = customer.CreatedAt,
                                        // Balance is deliberately omitted
                                    };

                                    uniqueCustomers.Add(sanitizedCustomer);
                                    processedIds.Add(customer.CustomerId);
                                }
                            }
                        }

                        // Update UI on dispatcher thread
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                // First clear existing results
                                FilteredCustomers.Clear();

                                // Then add each unique customer individually to ensure proper collection change notifications
                                foreach (var customer in uniqueCustomers)
                                {
                                    FilteredCustomers.Add(customer);
                                }

                                // Check if we found any results
                                if (!FilteredCustomers.Any() && !string.IsNullOrWhiteSpace(searchText))
                                {
                                    // Show a notification message
                                    SearchMessage = $"No customers found matching '{searchText}'";
                                    IsSearchMessageVisible = true;
                                    IsCustomerSearchVisible = false;
                                }
                                else
                                {
                                    // Only show if we have results AND not suppressing AND text doesn't match selection
                                    bool shouldShowDropdown = FilteredCustomers.Any() &&
                                                            !_suppressCustomerDropdown &&
                                                            !(SelectedCustomer != null &&
                                                                SelectedCustomer.Name?.Equals(searchText,
                                                                                            StringComparison.OrdinalIgnoreCase) == true);

                                    IsCustomerSearchVisible = shouldShowDropdown;
                                    IsSearchMessageVisible = false;
                                }

                                OnPropertyChanged(nameof(FilteredCustomers));
                                OnPropertyChanged(nameof(IsCustomerSearchVisible));
                                OnPropertyChanged(nameof(IsSearchMessageVisible));
                                OnPropertyChanged(nameof(SearchMessage));
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error updating customer search UI: {ex.Message}");
                            }
                        });
                    }
                    catch (Exception searchEx)
                    {
                        if (searchEx is TaskCanceledException or OperationCanceledException)
                        {
                            Debug.WriteLine("Customer search was canceled");
                            return;
                        }

                        Debug.WriteLine($"Error during customer search: {searchEx.Message}");
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                SearchMessage = "Error searching for customers. Please try again.";
                                IsSearchMessageVisible = true;
                                IsCustomerSearchVisible = false;

                                OnPropertyChanged(nameof(SearchMessage));
                                OnPropertyChanged(nameof(IsSearchMessageVisible));
                                OnPropertyChanged(nameof(IsCustomerSearchVisible));
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error updating UI after search error: {ex.Message}");
                            }
                        });
                    }
                    finally
                    {
                        // Always hide loading indicator
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            IsSearching = false;
                            OnPropertyChanged(nameof(IsSearching));
                        });
                    }
                }, "Searching customers", "CustomerSearch");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Customer search operation was canceled");
                IsSearching = false;
                OnPropertyChanged(nameof(IsSearching));
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsSearching = false;
                    OnPropertyChanged(nameof(IsSearching));

                    if (ex is TaskCanceledException or OperationCanceledException)
                    {
                        Debug.WriteLine("Search task was canceled");
                        return;
                    }

                    Debug.WriteLine($"Error in SearchCustomers: {ex.Message}");

                    // Show user-friendly error popup
                    SearchMessage = "An error occurred during search. Please try again.";
                    IsSearchMessageVisible = true;
                    IsCustomerSearchVisible = false;

                    OnPropertyChanged(nameof(SearchMessage));
                    OnPropertyChanged(nameof(IsSearchMessageVisible));
                    OnPropertyChanged(nameof(IsCustomerSearchVisible));
                });
            }
        }

        private async void SearchProducts()
        {
            try
            {
                await ExecuteOperationSafelyAsync(async () =>
                {
                    if (string.IsNullOrWhiteSpace(ProductSearchText))
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            FilteredProducts.Clear();
                            IsProductSearchVisible = false;
                            OnPropertyChanged(nameof(FilteredProducts));
                            OnPropertyChanged(nameof(IsProductSearchVisible));
                        });
                        return;
                    }

                    // Update status to show we're searching
                    StatusMessage = $"Searching for: {ProductSearchText}";
                    OnPropertyChanged(nameof(StatusMessage));

                    var products = await _productService.GetAllAsync();

                    // Get only active products matching the search criteria
                    var filtered = products
                        .Where(p => p?.IsActive == true) // Filter for active products only
                        .Where(p =>
                            p.Name?.Contains(ProductSearchText, StringComparison.OrdinalIgnoreCase) == true ||
                            p.CategoryName?.Contains(ProductSearchText, StringComparison.OrdinalIgnoreCase) == true ||
                            p.Barcode?.Contains(ProductSearchText, StringComparison.OrdinalIgnoreCase) == true)
                        .Take(10)  // Limit results for better performance
                        .ToList();

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        FilteredProducts = new ObservableCollection<ProductDTO>(filtered);
                        IsProductSearchVisible = FilteredProducts.Any();

                        OnPropertyChanged(nameof(FilteredProducts));
                        OnPropertyChanged(nameof(IsProductSearchVisible));

                        StatusMessage = filtered.Any()
                            ? $"Found {filtered.Count} product(s)"
                            : "No products found";
                        OnPropertyChanged(nameof(StatusMessage));
                    });
                }, "Searching products", "ProductSearch");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching products: {ex.Message}");
                await WindowManager.InvokeAsync(() =>
                    MessageBox.Show(
                        "An error occurred while searching products. Please try again.",
                        "Search Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));

                StatusMessage = "Error searching products";
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public void OnProductSelected(ProductDTO product)
        {
            if (product != null)
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        AddProductToTransaction(product);
                        ProductSearchText = string.Empty;
                        IsProductSearchVisible = false;

                        OnPropertyChanged(nameof(ProductSearchText));
                        OnPropertyChanged(nameof(IsProductSearchVisible));
                        OnPropertyChanged(nameof(CurrentTransaction));

                        StatusMessage = $"Added: {product.Name}";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error selecting product: {ex.Message}");
                        WindowManager.ShowWarning($"Error adding product: {ex.Message}");
                    }
                });
            }
            else
            {
                WindowManager.ShowWarning("Please select a product first");
            }
        }

        public void CloseProductSearch()
        {
            IsProductSearchVisible = false;
            ProductSearchText = string.Empty;
            OnPropertyChanged(nameof(IsProductSearchVisible));
            OnPropertyChanged(nameof(ProductSearchText));
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

                // Get owner window for dialogs
                var ownerWindow = GetOwnerWindow();
                if (ownerWindow == null)
                {
                    throw new InvalidOperationException("Cannot find a valid window to show dialogs");
                }

                // First dialog for customer name
                var nameDialog = new InputDialog("New Customer", "Enter customer name")
                {
                    Owner = ownerWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var nameResult = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    nameDialog.ShowDialog());

                if (nameResult == true)
                {
                    // Validate customer name
                    if (string.IsNullOrWhiteSpace(nameDialog.Input))
                    {
                        throw new InvalidOperationException("Customer name cannot be empty.");
                    }

                    if (nameDialog.Input.Length > 50)
                    {
                        await WindowManager.InvokeAsync(() => MessageBox.Show(
                            "Please enter a valid customer name. The name has been limited to 50 characters.",
                            "Name Too Long",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));

                        nameDialog.Input = nameDialog.Input.Substring(0, 50);
                    }

                    newCustomer.Name = nameDialog.Input;

                    // Second dialog for phone
                    var phoneDialog = new InputDialog("New Customer", "Enter customer phone")
                    {
                        Owner = ownerWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    var phoneResult = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        phoneDialog.ShowDialog());

                    if (phoneResult == true)
                    {
                        newCustomer.Phone = phoneDialog.Input;

                        // Third dialog for email (optional)
                        var emailDialog = new InputDialog("New Customer", "Enter customer email (optional)")
                        {
                            Owner = ownerWindow,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        };

                        var emailResult = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            emailDialog.ShowDialog());

                        if (emailResult == true)
                        {
                            newCustomer.Email = emailDialog.Input;
                        }

                        // Create the customer if we have at least a name
                        if (!string.IsNullOrWhiteSpace(newCustomer.Name))
                        {
                            StatusMessage = "Creating customer...";
                            OnPropertyChanged(nameof(StatusMessage));

                            var createdCustomer = await _customerService.CreateAsync(newCustomer);
                            if (createdCustomer != null)
                            {
                                // Set as selected customer
                                SelectedCustomer = createdCustomer;
                                CustomerSearchText = createdCustomer.Name;

                                // Hide dropdown
                                IsCustomerSearchVisible = false;

                                // Force UI update
                                OnPropertyChanged(nameof(SelectedCustomer));
                                OnPropertyChanged(nameof(CustomerSearchText));
                                OnPropertyChanged(nameof(IsCustomerSearchVisible));

                                await ShowSuccessMessage("Customer created successfully");

                                StatusMessage = $"Customer '{createdCustomer.Name}' created";
                                OnPropertyChanged(nameof(StatusMessage));
                            }
                            else
                            {
                                throw new InvalidOperationException("Failed to create customer");
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
                // Get the item to modify (either selected or last)
                var selectedItems = CurrentTransaction.Details.Where(d => d.IsSelected).ToList();
                var itemToModify = selectedItems.FirstOrDefault();

                // If no item is selected, use the last item
                if (itemToModify == null)
                {
                    itemToModify = CurrentTransaction.Details.LastOrDefault();
                    if (itemToModify == null)
                    {
                        throw new InvalidOperationException("No item available to modify");
                    }
                }

                // Get the owner window for the dialog
                var ownerWindow = GetOwnerWindow();
                if (ownerWindow == null)
                {
                    throw new InvalidOperationException("Cannot find a valid window to show dialog");
                }

                // Create and show the quantity dialog
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var dialog = new QuantityDialog(itemToModify.ProductName ?? "Selected Item", itemToModify.Quantity)
                        {
                            Owner = ownerWindow,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            // Validate new quantity
                            if (dialog.NewQuantity <= 0)
                            {
                                MessageBox.Show(
                                    "Quantity must be a positive number. It has been corrected to 1.",
                                    "Invalid Quantity",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                                dialog.NewQuantity = 1;
                            }

                            // Save original quantity for status message
                            int originalQuantity = itemToModify.Quantity;

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

                            // Update status message
                            StatusMessage = $"Changed quantity of '{itemToModify.ProductName}' from {originalQuantity} to {dialog.NewQuantity}";
                            OnPropertyChanged(nameof(StatusMessage));
                        }
                        else
                        {
                            StatusMessage = "Quantity change cancelled";
                            OnPropertyChanged(nameof(StatusMessage));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating quantity: {ex.Message}");
                        MessageBox.Show("An unexpected error occurred. Please try again.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);

                        StatusMessage = "Error changing quantity";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                });
            }, "Changing item quantity");
        }
    }
}