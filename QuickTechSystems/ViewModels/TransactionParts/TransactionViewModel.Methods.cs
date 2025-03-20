using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Domain.Enums;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel
    {
        private void InitializeCollections()
        {
            FilteredCustomers = new ObservableCollection<CustomerDTO>();
            HeldTransactions = new ObservableCollection<TransactionDTO>();
            CurrentTransaction = new TransactionDTO
            {
                Details = new ObservableCollection<TransactionDetailDTO>()
            };
        }

        private Window GetOwnerWindow()
        {
            // Try to get the active window first
            var activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (activeWindow != null)
                return activeWindow;

            // Fall back to the main window
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.IsLoaded)
                return mainWindow;

            // Last resort, get any window that's visible
            return System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                   ?? System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault();
        }

        public async Task LoadLatestTransactionIdAsync()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                try
                {
                    // Get the latest transaction ID
                    int latestId = await _transactionService.GetLatestTransactionIdAsync();

                    if (latestId > 0)
                    {
                        // Calculate the next transaction number (latest + 1)
                        int nextTransactionId = latestId + 1;

                        // Set the lookup field to this next number
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            LookupTransactionId = nextTransactionId.ToString();
                        });

                        Debug.WriteLine($"Latest transaction ID: {latestId}, Next transaction ID: {nextTransactionId}");
                    }
                    else
                    {
                        // If no transactions found, set to 1
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            LookupTransactionId = "1";
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading next transaction ID: {ex.Message}");
                    // Set a default value if we couldn't load the latest ID
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        LookupTransactionId = "1";
                    });
                }
            }, "Loading next transaction ID");
        }

        private async Task LookupTransactionAsync()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                // Clear any existing lookup operation flag
                IsEditingTransaction = false;

                if (string.IsNullOrWhiteSpace(LookupTransactionId))
                {
                    throw new InvalidOperationException("Please enter a valid transaction ID");
                }

                if (!int.TryParse(LookupTransactionId, out int transactionId))
                {
                    throw new InvalidOperationException("Transaction ID must be a number");
                }

                // Clear any current transaction data first
                ClearTransaction();

                // Explicitly clear customer information
                SelectedCustomer = null;
                CustomerSearchText = string.Empty;
                // Ensure dropdown is closed
                IsCustomerSearchVisible = false;
                OnPropertyChanged(nameof(SelectedCustomer));
                OnPropertyChanged(nameof(CustomerSearchText));
                OnPropertyChanged(nameof(IsCustomerSearchVisible));

                // Get the transaction by ID
                var transaction = await _transactionService.GetByIdAsync(transactionId);

                // If transaction doesn't exist, just show a message and don't throw an exception
                if (transaction == null)
                {
                    StatusMessage = $"Transaction #{transactionId} not found - Ready for new entry";
                    return; // Exit without error
                }

                if (transaction.Status != TransactionStatus.Completed && transaction.Status != TransactionStatus.Pending)
                {
                    throw new InvalidOperationException($"Transaction #{transactionId} cannot be edited (Status: {transaction.Status})");
                }

                // Store original transaction ID for reference
                int originalTransactionId = transaction.TransactionId;

                // Update the current transaction with the retrieved data
                CurrentTransaction = transaction;
                CurrentTransactionNumber = transaction.TransactionId.ToString();

                // Populate customer information ONLY if available
                if (transaction.CustomerId.HasValue && transaction.CustomerId.Value > 0)
                {
                    var customer = await _customerService.GetByIdAsync(transaction.CustomerId.Value);
                    if (customer != null)
                    {
                        // Temporarily disable search trigger
                        _isNavigating = true;

                        // Set the customer directly without showing dropdown
                        SelectedCustomer = customer;
                        CustomerSearchText = customer.Name;

                        // Explicitly ensure dropdown is closed
                        IsCustomerSearchVisible = false;

                        // Re-enable search trigger after a short delay
                        await Task.Delay(100);
                        _isNavigating = false;
                    }
                }

                // Update totals
                UpdateTotals();

                // Set editing state
                IsEditingTransaction = true;

                // Update UI
                StatusMessage = $"Editing Transaction #{originalTransactionId}";

                // Log the action
                Debug.WriteLine($"Loaded Transaction #{originalTransactionId} for editing");
            }, "Looking up transaction", "Transaction loaded for editing");
        }

        // Helper method to explicitly trigger customer search
        private async Task TriggerCustomerSearchAsync(string searchText)
        {
            try
            {
                // Only proceed if there's text to search
                if (string.IsNullOrWhiteSpace(searchText)) return;

                // Directly query customer service for matching customers
                var customers = await _customerService.GetByNameAsync(searchText);

                // Deduplicate by CustomerID
                var uniqueCustomers = customers
                    .GroupBy(c => c.CustomerId)
                    .Select(g => g.First())
                    .ToList();

                // Create new customer objects without balance information
                var sanitizedCustomers = uniqueCustomers.Select(c => new CustomerDTO
                {
                    CustomerId = c.CustomerId,
                    Name = c.Name,
                    Phone = c.Phone,
                    Email = c.Email,
                    Address = c.Address,
                    IsActive = c.IsActive,
                    CreatedAt = c.CreatedAt,
                    // Balance is not needed
                }).ToList();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Clear existing results first
                    FilteredCustomers.Clear();

                    // Update filtered customers collection
                    foreach (var customer in sanitizedCustomers)
                    {
                        FilteredCustomers.Add(customer);
                    }

                    // Make customer search results visible
                    IsCustomerSearchVisible = FilteredCustomers.Any();

                    // Force UI updates
                    OnPropertyChanged(nameof(FilteredCustomers));
                    OnPropertyChanged(nameof(IsCustomerSearchVisible));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error triggering customer search: {ex.Message}");
            }
        }
        private async void ShowInvalidLookupAlert()
        {
            await WindowManager.InvokeAsync(() =>
            {
                MessageBox.Show(
                    "Invalid transaction number. Please enter a numeric value.",
                    "Invalid Input",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });

            // Update status message
            StatusMessage = "Loading latest transaction number...";

            // Reset to the latest transaction ID
            await LoadLatestTransactionIdAsync();

            // Update status message again
            StatusMessage = "Ready for new transaction";
        }
        private async Task UpdateLookupTransactionIdAsync()
        {
            try
            {
                // Get the latest transaction ID
                int latestId = await _transactionService.GetLatestTransactionIdAsync();

                if (latestId > 0)
                {
                    // Set to the next transaction number
                    LookupTransactionId = (latestId + 1).ToString();
                    Debug.WriteLine($"Updated lookup transaction ID to: {LookupTransactionId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating lookup transaction ID: {ex.Message}");
                // Don't throw exception as this is a convenience feature
            }
        }


        private async Task UpdateExistingTransactionAsync()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No items in transaction to update");
                }

                // Validate active drawer
                var drawer = await _drawerService.GetCurrentDrawerAsync();
                if (drawer == null)
                {
                    throw new InvalidOperationException("No active cash drawer. Please open a drawer first.");
                }

                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    // Prepare the transaction for update
                    CurrentTransaction.TransactionDate = DateTime.Now;
                    CurrentTransaction.CustomerId = SelectedCustomer?.CustomerId;
                    CurrentTransaction.CustomerName = SelectedCustomer?.Name ?? "Walk-in Customer";
                    CurrentTransaction.TotalAmount = TotalAmount;
                    CurrentTransaction.Status = TransactionStatus.Completed;
                    CurrentTransaction.PaymentMethod = "Cash"; // Setting payment method for cash payment

                    // Process the updated transaction
                    var updatedTransaction = await _transactionService.UpdateAsync(CurrentTransaction);

                    // Publish event for the updated transaction
                    _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>(
                        "Update",
                        updatedTransaction));

                    // Commit the database transaction
                    await transaction.CommitAsync();

                    // Print updated receipt
                    await PrintReceipt();

                    // Start a new transaction (which will reset IsEditingTransaction)
                    StartNewTransaction();

                    // Update the lookup transaction ID with the next transaction number
                    await UpdateLookupTransactionIdAsync();

                    StatusMessage = $"Transaction #{updatedTransaction.TransactionId} updated successfully";
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new InvalidOperationException($"Failed to update transaction: {ex.Message}", ex);
                }
            }, "Updating transaction", "Transaction updated successfully");
        }

        private void FilterProductsByCategory(CategoryDTO category)
        {
            if (_allProducts == null) return;

            // Log what we're doing
            Debug.WriteLine($"Filtering products by category: {category?.Name ?? "null"}");

            if (category == null || category.Name == "All")
            {
                // Show all ACTIVE products
                FilteredProducts = new ObservableCollection<ProductDTO>(_allProducts.Where(p => p.IsActive));
            }
            else
            {
                // Show only ACTIVE products from the selected category
                var filteredList = _allProducts.Where(p => p.CategoryId == category.CategoryId && p.IsActive).ToList();
                FilteredProducts = new ObservableCollection<ProductDTO>(filteredList);
            }

            OnPropertyChanged(nameof(FilteredProducts));
            Debug.WriteLine($"Filtered products by category: {category?.Name ?? "All"}, count: {FilteredProducts.Count}");
        }

        // Add this method to TransactionViewModel.Methods.cs
        public async Task LoadRestaurantModePreference()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (_systemPreferencesService == null)
                {
                    Debug.WriteLine("SystemPreferencesService is not initialized");
                    return;
                }

                const string userId = "default";
                string restaurantModeStr = await _systemPreferencesService.GetPreferenceValueAsync(
                    userId, "RestaurantMode", "false");

                bool isRestaurantMode = bool.Parse(restaurantModeStr);

                // Update properties on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsProductCardsVisible = isRestaurantMode;
                    IsRestaurantMode = isRestaurantMode;

                    // Force UI update
                    OnPropertyChanged(nameof(IsProductCardsVisible));
                    OnPropertyChanged(nameof(IsRestaurantMode));

                    Debug.WriteLine($"Restaurant mode loaded from preferences: {isRestaurantMode}");
                });

                // Load categories if in restaurant mode
                if (isRestaurantMode && (ProductCategories == null || ProductCategories.Count == 0))
                {
                    await LoadProductCategoriesAsync();
                }
            }, "Loading restaurant mode preference");
        }

        private void OnApplicationModeChanged(ApplicationModeChangedEvent evt)
        {
            // Update on UI thread
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Update the view mode
                IsProductCardsVisible = evt.IsRestaurantMode;
                IsRestaurantMode = evt.IsRestaurantMode;

                // Force UI refresh
                OnPropertyChanged(nameof(IsProductCardsVisible));
                OnPropertyChanged(nameof(IsRestaurantMode));

                // Load categories if needed
                if (evt.IsRestaurantMode && (ProductCategories == null || ProductCategories.Count == 0))
                {
                    LoadProductCategoriesAsync().ConfigureAwait(false);
                }
            });
        }

        public void AddProductToTransactionWithQuantity(ProductDTO product, int quantity)
        {
            // Validate product and quantity
            if (product == null)
            {
                WindowManager.ShowWarning("No product selected. Please select a product first.");
                return;
            }

            // Validate quantity is positive
            if (quantity <= 0)
            {
                WindowManager.ShowWarning("Quantity must be a positive number. It has been corrected to 1.");
                quantity = 1;
            }

            // Check if adding this product would result in low stock
            CheckAndAlertLowStock(product, quantity);

            if (CurrentTransaction?.Details == null)
            {
                CurrentTransaction = new TransactionDTO
                {
                    Details = new ObservableCollection<TransactionDetailDTO>(),
                    TransactionDate = DateTime.Now,
                    Status = TransactionStatus.Pending
                };
            }

            var existingDetail = CurrentTransaction.Details.FirstOrDefault(d => d.ProductId == product.ProductId);
            if (existingDetail != null)
            {
                existingDetail.Quantity += quantity;
                existingDetail.Total = existingDetail.Quantity * existingDetail.UnitPrice;
                var index = CurrentTransaction.Details.IndexOf(existingDetail);
                CurrentTransaction.Details.RemoveAt(index);
                CurrentTransaction.Details.Insert(index, existingDetail);
            }
            else
            {
                // Get customer-specific price if available
                decimal unitPrice = product.SalePrice;
                if (_customerSpecificPrices.TryGetValue(product.ProductId, out decimal customPrice))
                {
                    unitPrice = customPrice;
                }

                var detail = new TransactionDetailDTO
                {
                    ProductId = product.ProductId,
                    ProductName = product.Name,
                    ProductBarcode = product.Barcode,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    PurchasePrice = product.PurchasePrice,
                    Total = unitPrice * quantity,
                    TransactionId = CurrentTransaction.TransactionId
                };
                CurrentTransaction.Details.Add(detail);
            }

            UpdateTotals();
            OnPropertyChanged(nameof(CurrentTransaction.Details));
        }

        private async void CheckAndAlertLowStock(ProductDTO product, int quantity)
        {
            // Only check for active products
            if (product == null || !product.IsActive) return;

            // Calculate the potential new stock level after this transaction
            int potentialNewStock = product.CurrentStock - quantity;

            // Check if the potential new stock is at or below the minimum stock level
            if (potentialNewStock <= product.MinimumStock)
            {
                // Display alert to the user
                await WindowManager.InvokeAsync(() =>
                    MessageBox.Show(
                        $"Warning: Product '{product.Name}' will reach low stock level.\n\nCurrent Stock: {product.CurrentStock}\nMinimum Stock: {product.MinimumStock}\nAfter Sale: {potentialNewStock}",
                        "Low Stock Alert",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning)
                );

                // Use proper async task handling
                try
                {
                    var currentUser = App.Current.Properties["CurrentUser"] as EmployeeDTO;
                    string cashierId = currentUser?.EmployeeId.ToString() ?? "0";
                    string cashierName = currentUser?.FullName ?? "Unknown";

                    // Check if App and ServiceProvider exist
                    if (App.Current is App app && app.ServiceProvider != null)
                    {
                        var lowStockHistoryService = app.ServiceProvider.GetService<ILowStockHistoryService>();
                        if (lowStockHistoryService != null)
                        {
                            await lowStockHistoryService.LogLowStockAlertAsync(
                                product.ProductId,
                                product.Name,
                                product.CurrentStock,
                                product.MinimumStock,
                                cashierId,
                                cashierName
                            );
                        }
                        else
                        {
                            Debug.WriteLine("LowStockHistoryService is not available");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error logging low stock alert: {ex.Message}");
                    // Continue with transaction processing despite logging error
                }
            }
        }

        private async Task CacheProductImagesAsync(IEnumerable<ProductDTO> products)
        {
            if (products == null) return;

            // Process images in batches to avoid UI freezing
            const int batchSize = 10;
            var productList = products.Where(p => p?.Image != null && !_imageCache.ContainsKey(p.ProductId)).ToList();

            for (int i = 0; i < productList.Count; i += batchSize)
            {
                var batch = productList.Skip(i).Take(batchSize);
                await Task.Run(() => {
                    foreach (var product in batch)
                    {
                        try
                        {
                            var image = new BitmapImage();
                            using (var ms = new MemoryStream(product.Image))
                            {
                                image.BeginInit();
                                image.CacheOption = BitmapCacheOption.OnLoad;
                                image.DecodePixelWidth = 150; // Optimize for display size
                                image.StreamSource = ms;
                                image.EndInit();
                                image.Freeze(); // Important for cross-thread access
                            }

                            // Update image cache on UI thread
                            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                                _imageCache[product.ProductId] = image;
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error caching image for product {product.ProductId}: {ex.Message}");
                        }
                    }
                });

                // Allow UI to breathe between batches
                await Task.Delay(10);
            }
        }

        private async void ToggleView()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                // If there's an ongoing transaction, warn the user
                if (CurrentTransaction?.Details?.Any() == true)
                {
                    var result = await WindowManager.InvokeAsync(() => MessageBox.Show(
                        "Switching views will not affect your current transaction, but any unsaved changes might be lost. Continue?",
                        "Confirm View Change",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question));

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                IsProductCardsVisible = !IsProductCardsVisible;
                IsRestaurantMode = IsProductCardsVisible;

                Debug.WriteLine($"Toggled view. IsProductCardsVisible: {IsProductCardsVisible}, IsRestaurantMode: {IsRestaurantMode}");

                // Save the preference
                const string userId = "default";
                await _systemPreferencesService.SavePreferenceAsync(userId, "RestaurantMode", IsRestaurantMode.ToString());

                // Reload products when switching to card view
                if (IsProductCardsVisible)
                {
                    await LoadDataAsync();

                    // Load product categories if needed
                    if (IsRestaurantMode && (ProductCategories == null || ProductCategories.Count == 0))
                    {
                        await LoadProductCategoriesAsync();
                    }
                }
            }, "Toggling view mode");
        }



        private static SemaphoreSlim _categoryLoadSemaphore = new SemaphoreSlim(1, 1);

        public async Task LoadProductCategoriesAsync()
        {
            try
            {
                // Wait for any previous operations to complete
                await _categoryLoadSemaphore.WaitAsync();

                Debug.WriteLine("Loading product categories...");

                try
                {
                    var categories = await _categoryService.GetProductCategoriesAsync();
                    Debug.WriteLine($"Loaded {categories.Count()} categories");

                    // Create a list with "All" category first
                    var allCategoriesList = new List<CategoryDTO>
            {
                new CategoryDTO { CategoryId = 0, Name = "All", Type = "Product" }
            };

                    // Add the rest of the categories
                    allCategoriesList.AddRange(categories);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ProductCategories = new ObservableCollection<CategoryDTO>(allCategoriesList);
                        Debug.WriteLine($"Set ProductCategories with {ProductCategories.Count} items");

                        // Select "All" category by default if no category is selected
                        if (SelectedCategory == null)
                        {
                            SelectedCategory = ProductCategories.FirstOrDefault();
                            Debug.WriteLine($"Selected default category: {SelectedCategory?.Name}");
                        }

                        // Force UI update
                        OnPropertyChanged(nameof(ProductCategories));
                        OnPropertyChanged(nameof(SelectedCategory));
                        OnPropertyChanged(nameof(IsRestaurantMode));
                    });
                }
                finally
                {
                    // Always release the semaphore
                    _categoryLoadSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading product categories: {ex.Message}");
                await ShowErrorMessageAsync($"Error loading product categories: {ex.Message}");
            }
        }
        private class DialogManager
        {
            private static readonly object _lockObj = new object();
            private static bool _isDialogOpen = false;

            public static async Task<bool> ShowDialogAsync(Func<Task<bool>> dialogAction)
            {
                lock (_lockObj)
                {
                    if (_isDialogOpen)
                        return false;

                    _isDialogOpen = true;
                }

                try
                {
                    return await dialogAction();
                }
                finally
                {
                    lock (_lockObj)
                    {
                        _isDialogOpen = false;
                    }
                }
            }
        }
        public void AddProductToTransaction(ProductDTO product, int quantity = 1)
        {
            // Validate product
            if (product == null)
            {
                WindowManager.ShowWarning("No product selected. Please select a product first.");
                return;
            }

            // Validate quantity is positive
            if (quantity <= 0)
            {
                WindowManager.ShowWarning("Quantity must be a positive number. It has been corrected to 1.");
                quantity = 1;
            }

            // Check if adding this product would result in low stock
            CheckAndAlertLowStock(product, quantity);

            if (CurrentTransaction?.Details == null)
            {
                CurrentTransaction = new TransactionDTO
                {
                    Details = new ObservableCollection<TransactionDetailDTO>(),
                    TransactionDate = DateTime.Now,
                    Status = TransactionStatus.Pending
                };
            }

            var existingDetail = CurrentTransaction.Details.FirstOrDefault(d => d.ProductId == product.ProductId);
            if (existingDetail != null)
            {
                existingDetail.Quantity += quantity;
                existingDetail.Total = existingDetail.Quantity * existingDetail.UnitPrice;
                var index = CurrentTransaction.Details.IndexOf(existingDetail);
                CurrentTransaction.Details.RemoveAt(index);
                CurrentTransaction.Details.Insert(index, existingDetail);
            }
            else
            {
                // Get customer-specific price if available
                decimal unitPrice = product.SalePrice;
                if (_customerSpecificPrices.TryGetValue(product.ProductId, out decimal customPrice))
                {
                    unitPrice = customPrice;
                }

                var detail = new TransactionDetailDTO
                {
                    ProductId = product.ProductId,
                    ProductName = product.Name,
                    ProductBarcode = product.Barcode,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    PurchasePrice = product.PurchasePrice,
                    Total = unitPrice * quantity,
                    TransactionId = CurrentTransaction.TransactionId
                };
                CurrentTransaction.Details.Add(detail);
            }

            UpdateTotals();
            OnPropertyChanged(nameof(CurrentTransaction.Details));
        }

        private async Task CloseDrawerAsync()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                // Check if there's an ongoing transaction
                if (CurrentTransaction?.Details?.Any() == true)
                {
                    var confirmResult = await WindowManager.InvokeAsync(() => MessageBox.Show(
                        "You have an ongoing transaction. Closing the drawer will cancel this transaction. Continue?",
                        "Unsaved Transaction",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning));

                    if (confirmResult != MessageBoxResult.Yes)
                        return;
                }

                // Add a second confirmation for drawer closing
                var secondConfirmResult = await WindowManager.InvokeAsync(() => MessageBox.Show(
                    "Are you sure you want to close the drawer? This will end your current session.",
                    "Confirm Drawer Close",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question));

                if (secondConfirmResult != MessageBoxResult.Yes)
                    return;

                var ownerWindow = GetOwnerWindow();

                var dialog = new InputDialog("Close Drawer", "Enter final cash amount:")
                {
                    Owner = ownerWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                bool? inputResult = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => dialog.ShowDialog());

                if (inputResult == true)
                {
                    if (!decimal.TryParse(dialog.Input, out decimal finalAmount))
                    {
                        throw new InvalidOperationException("Please enter a valid amount. Amount has been set to 0.");
                    }

                    await _drawerService.CloseDrawerAsync(finalAmount, "Closed by user at end of shift");

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            ownerWindow,
                            "Drawer closed successfully. The application will now exit.",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });

                    // Close the application after showing the success message
                    System.Windows.Application.Current.Shutdown();
                }
            }, "Closing drawer");
        }
        private void StartNewTransaction()
        {
            var currentUser = App.Current.Properties["CurrentUser"] as EmployeeDTO;

            CurrentTransactionNumber = DateTime.Now.ToString("yyyyMMddHHmmss");
            CurrentTransaction = new TransactionDTO
            {
                TransactionDate = DateTime.Now,
                Status = TransactionStatus.Pending,
                Details = new ObservableCollection<TransactionDetailDTO>(),
                CashierId = currentUser?.EmployeeId.ToString() ?? "0",
                CashierName = currentUser?.FullName ?? "Unknown"
            };

            // Update the UI with cashier info
            CashierName = currentUser?.FullName ?? "Unknown";

            // Reset editing state
            IsEditingTransaction = false;

            ClearTotals();
        }

        private async Task HoldTransaction()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (CurrentTransaction?.Details.Any() == true)
                {
                    CurrentTransaction.Status = TransactionStatus.Pending;
                    await Task.Run(() => HeldTransactions.Add(CurrentTransaction));
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show("Transaction has been held successfully", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information));
                    StartNewTransaction();
                }
                else
                {
                    throw new InvalidOperationException("No items in transaction to hold");
                }
            }, "Holding transaction");
        }

        private async Task SaveAsQuoteAsync()
        {
            await ShowLoadingAsync("Saving quote...", async () =>
            {
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No items to save as quote");
                }

                if (SelectedCustomer == null)
                {
                    throw new InvalidOperationException("Please select a customer");
                }

                // Get the current main window to use as owner for any dialogs
                Window ownerWindow = GetOwnerWindow();

                var quoteToCreate = new QuoteDTO
                {
                    CustomerId = SelectedCustomer.CustomerId,
                    CustomerName = SelectedCustomer.Name,
                    TotalAmount = TotalAmount,
                    CreatedDate = DateTime.Now,
                    ExpiryDate = DateTime.Now.AddDays(30), // Or whatever expiry period you want
                    Status = "Pending",
                    QuoteNumber = $"Q-{DateTime.Now:yyyyMMddHHmmss}",
                    Details = new ObservableCollection<QuoteDetailDTO>(
                        CurrentTransaction.Details.Select(d => new QuoteDetailDTO
                        {
                            ProductId = d.ProductId,
                            ProductName = d.ProductName,
                            Quantity = d.Quantity,
                            UnitPrice = d.UnitPrice,
                            Total = d.Total
                        }))
                };

                var result = await _quoteService.CreateAsync(quoteToCreate);
                StartNewTransaction();
            }, "Quote saved successfully");
        }


        private async Task RecallTransaction()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                // Check if there's an ongoing transaction
                if (CurrentTransaction?.Details?.Any() == true)
                {
                    var result = await WindowManager.InvokeAsync(() => MessageBox.Show(
                        "You have items in the current transaction. Recalling a held transaction will replace these items. Continue?",
                        "Unsaved Transaction",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning));

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                var heldTransaction = HeldTransactions.LastOrDefault();
                if (heldTransaction != null)
                {
                    await Task.Run(() =>
                    {
                        CurrentTransaction = heldTransaction;
                        HeldTransactions.Remove(heldTransaction);
                    });
                    UpdateTotals();
                }
                else
                {
                    throw new InvalidOperationException("No held transactions to recall");
                }
            }, "Recalling transaction");
        }

        private async Task UpdateUI(Action action)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(action);
        }

        private async Task VoidTransaction()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (CurrentTransaction == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No transaction to void");
                }

                if (MessageBox.Show("Are you sure you want to void this transaction?", "Confirm Void",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    await _transactionService.UpdateStatusAsync(CurrentTransaction.TransactionId, TransactionStatus.Cancelled);
                    StartNewTransaction();
                }
            }, "Voiding transaction");
        }

        private async Task ShowErrorMessage(string message)
        {
            await WindowManager.InvokeAsync(() =>
            {
                var ownerWindow = GetOwnerWindow();
                // Add null check before using
                MessageBox.Show(
                    ownerWindow ?? System.Windows.Application.Current.MainWindow,
                    message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }

        private async Task ShowSuccessMessage(string message)
        {
            await WindowManager.InvokeAsync(() =>
            {
                var ownerWindow = GetOwnerWindow();
                MessageBox.Show(
                    ownerWindow,
                    message,
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
        }

        public async Task ProcessBarcodeInput()
        {
            if (string.IsNullOrEmpty(BarcodeText)) return;

            // Store barcode text in case we need to restore it after clearing
            string barcodeInput = BarcodeText.Trim();

            // Clear barcode input field immediately for better UX
            BarcodeText = string.Empty;
            OnPropertyChanged(nameof(BarcodeText));

            await ExecuteOperationSafelyAsync(async () =>
            {
                // Set status message while processing
                StatusMessage = $"Processing barcode: {barcodeInput}";
                OnPropertyChanged(nameof(StatusMessage));

                // Try to get product by barcode
                var product = await _productService.GetByBarcodeAsync(barcodeInput);

                if (product == null)
                {
                    // Try alternate format (some scanners add characters)
                    string alternateFormat = barcodeInput.TrimStart('0');
                    if (!string.IsNullOrEmpty(alternateFormat) && alternateFormat != barcodeInput)
                    {
                        product = await _productService.GetByBarcodeAsync(alternateFormat);
                    }

                    // If still not found
                    if (product == null)
                    {
                        await WindowManager.InvokeAsync(() =>
                            MessageBox.Show($"Barcode '{barcodeInput}' is not registered. Please check.",
                                           "Unknown Barcode",
                                           MessageBoxButton.OK,
                                           MessageBoxImage.Warning));

                        StatusMessage = "Ready";
                        OnPropertyChanged(nameof(StatusMessage));
                        return;
                    }
                }

                // Only process the product if it exists and is active
                if (product.IsActive)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        AddProductToTransaction(product);
                        StatusMessage = $"Added: {product.Name}";
                        OnPropertyChanged(nameof(StatusMessage));
                    });
                }
                else
                {
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show($"Product '{product.Name}' is not available for sale at the moment.",
                                       "Inactive Product",
                                       MessageBoxButton.OK,
                                       MessageBoxImage.Warning));

                    StatusMessage = "Ready";
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }, "Processing barcode input");
        }

        private async Task LoadCustomerSpecificPrices()
        {
            try
            {
                await ExecuteOperationSafelyAsync(async () =>
                {
                    // Default to empty dictionary
                    var newPrices = new Dictionary<int, decimal>();

                    if (SelectedCustomer != null)
                    {
                        try
                        {
                            var customerPrices = await _customerService.GetCustomProductPricesAsync(SelectedCustomer.CustomerId);
                            if (customerPrices != null)
                            {
                                newPrices = customerPrices.ToDictionary(
                                    cpp => cpp.ProductId,
                                    cpp => cpp.Price
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error getting customer prices: {ex.Message}");
                            // Continue with empty pricing dictionary rather than failing
                        }
                    }

                    // Update the prices dictionary
                    CustomerSpecificPrices = newPrices;

                    // Update prices for existing items in cart
                    if (CurrentTransaction?.Details != null)
                    {
                        bool pricesChanged = false;
                        foreach (var detail in CurrentTransaction.Details)
                        {
                            pricesChanged |= UpdateProductPrice(detail);
                        }

                        if (pricesChanged)
                        {
                            UpdateTotals();

                            // Notify UI of updated items
                            OnPropertyChanged(nameof(CurrentTransaction.Details));
                        }
                    }
                }, "Loading customer specific prices");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading customer prices: {ex.Message}");
                StatusMessage = "Error loading customer prices";
                CustomerSpecificPrices = new Dictionary<int, decimal>();
            }
        }


        private bool UpdateProductPrice(TransactionDetailDTO detail)
        {
            if (_customerSpecificPrices.TryGetValue(detail.ProductId, out decimal customPrice))
            {
                if (detail.UnitPrice != customPrice)
                {
                    decimal oldTotal = detail.Total;
                    detail.UnitPrice = customPrice;
                    detail.Total = detail.Quantity * customPrice;

                    // Return true if price was actually changed
                    return true;
                }
            }
            return false;
        }

        private async Task<bool?> ShowDialog<T>(T dialog) where T : Window
        {
            return await WindowManager.InvokeAsync(() => {
                dialog.Owner = GetOwnerWindow();
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                return dialog.ShowDialog();
            });
        }

        private async Task<string> ShowInputDialog(string prompt, string title)
        {
            return await WindowManager.InvokeAsync(() =>
            {
                var dialog = new InputDialog(title, prompt)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                return dialog.ShowDialog() == true ? dialog.Input : string.Empty;
            });
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            await WindowManager.InvokeAsync(() =>
            {
                var ownerWindow = GetOwnerWindow();
                MessageBox.Show(
                    ownerWindow,
                    message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }

        private async Task UpdateFinancialSummaryAsync()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                var startDate = DateTime.Today;
                var endDate = DateTime.Now;
                var summary = await _drawerService.GetFinancialSummaryAsync(startDate, endDate);

                DailySales = summary.Sales;
                SupplierPayments = summary.SupplierPayments;
                DailyExpenses = summary.Expenses;

                NetSales = DailySales;
                NetCashflow = DailySales - SupplierPayments - DailyExpenses;

                OnPropertyChanged(nameof(DailySales));
                OnPropertyChanged(nameof(SupplierPayments));
                OnPropertyChanged(nameof(DailyExpenses));
                OnPropertyChanged(nameof(NetSales));
                OnPropertyChanged(nameof(NetCashflow));
            }, "Updating financial summary");
        }

        private void IncrementTransactionId()
        {
            if (string.IsNullOrWhiteSpace(LookupTransactionId))
            {
                LookupTransactionId = "1";
            }
            else if (int.TryParse(LookupTransactionId, out int currentId))
            {
                LookupTransactionId = (currentId + 1).ToString();

                // Automatically trigger lookup
                LookupTransactionAsync().ConfigureAwait(false);
            }
        }

        private void DecrementTransactionId()
        {
            if (int.TryParse(LookupTransactionId, out int currentId) && currentId > 1)
            {
                LookupTransactionId = (currentId - 1).ToString();

                // Automatically trigger lookup
                LookupTransactionAsync().ConfigureAwait(false);
            }
        }

        private async Task InitializeProductsAsync()
        {
            try
            {
                var products = await _productService.GetAllAsync();

                // Filter to only include active products
                var activeProducts = products.Where(p => p.IsActive).ToList();
                AllProducts = new ObservableCollection<ProductDTO>(activeProducts);

                // Initialize FilteredProducts with only Internet category active products
                var internetProducts = activeProducts
                    .Where(p => p.CategoryName?.Contains("Internet", StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
                FilteredProducts = new ObservableCollection<ProductDTO>(internetProducts);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading products: {ex.Message}");
            }
        }

        private void FilterProductsForDropdown(string searchText)
        {
            if (_allProducts == null)
            {
                FilteredProducts = new ObservableCollection<ProductDTO>();
                Debug.WriteLine("No products available to filter");
                return;
            }

            // Always start with only active products
            var filteredList = _allProducts.Where(p => p.IsActive).ToList();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filteredList = filteredList.Where(p =>
                    p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.Barcode.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            FilteredProducts = new ObservableCollection<ProductDTO>(filteredList);
            Debug.WriteLine($"Filtered products count: {FilteredProducts.Count}");
        }

        private void CacheProductImage(ProductDTO product)
        {
            if (product?.Image == null || _imageCache.ContainsKey(product.ProductId))
                return;

            try
            {
                var image = new BitmapImage();
                using (var ms = new MemoryStream(product.Image))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                    image.Freeze();
                }
                _imageCache[product.ProductId] = image;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error caching image for product {product.ProductId}: {ex.Message}");
                // Don't throw - just log the error and continue
            }
        }
    }
}