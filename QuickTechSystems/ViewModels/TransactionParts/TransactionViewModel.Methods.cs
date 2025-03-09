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
                        LookupTransactionId = nextTransactionId.ToString();

                        Debug.WriteLine($"Latest transaction ID: {latestId}, Next transaction ID: {nextTransactionId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading next transaction ID: {ex.Message}");
                    // Don't show error to user since this is just a convenience feature
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

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Update filtered customers collection
                    FilteredCustomers = new ObservableCollection<CustomerDTO>(customers);

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
            if (product == null || quantity <= 0) return;

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

                // Log the low stock alert in a separate thread to prevent blocking
                Task.Run(async () => {
                    try
                    {
                        var currentUser = App.Current.Properties["CurrentUser"] as EmployeeDTO;
                        string cashierId = currentUser?.EmployeeId.ToString() ?? "0";
                        string cashierName = currentUser?.FullName ?? "Unknown";

                        var lowStockHistoryService = ((App)App.Current).ServiceProvider.GetRequiredService<ILowStockHistoryService>();
                        await lowStockHistoryService.LogLowStockAlertAsync(
                            product.ProductId,
                            product.Name,
                            product.CurrentStock,
                            product.MinimumStock,
                            cashierId,
                            cashierName
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error logging low stock alert: {ex.Message}");
                        // Continue with transaction processing despite logging error
                    }
                });
            }
        }


        private async void ToggleView()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
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

        public void AddProductToTransaction(ProductDTO product, int quantity = 1)
        {
            if (product == null || quantity <= 0) return;

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


        private bool CanAddToCustomerDebt()
        {
            return SelectedCustomer != null &&
                   CurrentTransaction?.Details != null &&
                   CurrentTransaction.Details.Any() &&
                   TotalAmount > 0;
        }

        private async Task AddToCustomerDebtAsync()
        {
            await ShowLoadingAsync("Processing debt transaction...", async () =>
            {
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No items to add to customer debt");
                }

                if (SelectedCustomer == null)
                {
                    throw new InvalidOperationException("Please select a customer");
                }

                // Validate customer's credit limit
                if (SelectedCustomer.CreditLimit > 0 &&
                    (SelectedCustomer.Balance + TotalAmount) > SelectedCustomer.CreditLimit)
                {
                    throw new InvalidOperationException(
                        $"Adding this debt would exceed the customer's credit limit of {SelectedCustomer.CreditLimit:C2}");
                }

                // Begin a single transaction for all operations
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    // Prepare transaction data
                    CurrentTransaction.TransactionDate = DateTime.Now;
                    CurrentTransaction.CustomerId = SelectedCustomer.CustomerId;
                    CurrentTransaction.CustomerName = SelectedCustomer.Name;
                    CurrentTransaction.TotalAmount = TotalAmount;
                    CurrentTransaction.PaidAmount = 0;
                    CurrentTransaction.Balance = TotalAmount;
                    CurrentTransaction.Status = TransactionStatus.Completed;
                    CurrentTransaction.TransactionType = TransactionType.Sale;
                    CurrentTransaction.PaymentMethod = "Debt";
                    CurrentTransaction.CashierId = GetCurrentUserId();
                    CurrentTransaction.CashierName = GetCurrentUserName();

                    // Process the sale transaction - this will handle the customer balance update
                    var processedTransaction = await _transactionService.ProcessSaleAsync(CurrentTransaction);

                    // Publish events for both the transaction and customer update
                    _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>("Create", processedTransaction));
                    _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>(
                        "Update",
                        new CustomerDTO
                        {
                            CustomerId = SelectedCustomer.CustomerId,
                            Name = SelectedCustomer.Name,
                            Balance = SelectedCustomer.Balance + TotalAmount
                        }));

                    // Commit transaction
                    await transaction.CommitAsync();

                    // Print receipt
                    await PrintReceipt();
                    StartNewTransaction();

                    // Notify UI of changes
                    OnPropertyChanged(nameof(CurrentTransaction));
                    OnPropertyChanged(nameof(TotalAmount));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new InvalidOperationException($"Failed to process debt transaction: {ex.Message}", ex);
                }
            }, "Transaction has been added to customer's debt");
            await UpdateLookupTransactionIdAsync();
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

        private async Task CloseDrawerAsync()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                var ownerWindow = GetOwnerWindow();

                var dialog = new InputDialog("Close Drawer", "Enter final cash amount:")
                {
                    Owner = ownerWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                bool? result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => dialog.ShowDialog());

                if (result == true && decimal.TryParse(dialog.Input, out decimal finalAmount))
                {
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
                if (CurrentTransaction == null) return;

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
                MessageBox.Show(GetOwnerWindow(),
                    message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error)
            );
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

            await ExecuteOperationSafelyAsync(async () =>
            {
                var product = await _productService.GetByBarcodeAsync(BarcodeText);

                // Only process the product if it exists and is active
                if (product != null && product.IsActive)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        AddProductToTransaction(product);
                    });
                }
                else if (product != null && !product.IsActive)
                {
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show("Product is inactive and cannot be added to the transaction",
                                        "Inactive Product",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning));
                }
                else
                {
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show("Product not found",
                                        "Error",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning));
                }

                BarcodeText = string.Empty;
            }, "Processing barcode input");
        }

        private async Task LoadCustomerSpecificPrices()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (SelectedCustomer == null)
                {
                    CustomerSpecificPrices = new Dictionary<int, decimal>();
                    return;
                }

                var customerPrices = await _customerService.GetCustomProductPricesAsync(SelectedCustomer.CustomerId);
                CustomerSpecificPrices = customerPrices.ToDictionary(
                    cpp => cpp.ProductId,
                    cpp => cpp.Price
                );

                // Update prices for existing items in cart
                if (CurrentTransaction?.Details != null)
                {
                    foreach (var detail in CurrentTransaction.Details)
                    {
                        UpdateProductPrice(detail);
                    }
                    UpdateTotals();
                }
            }, "Loading customer specific prices");
        }

        private void UpdateProductPrice(TransactionDetailDTO detail)
        {
            if (_customerSpecificPrices.TryGetValue(detail.ProductId, out decimal customPrice))
            {
                detail.UnitPrice = customPrice;
                detail.Total = detail.Quantity * customPrice;
            }
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
                DailyReturns = Math.Abs(summary.Returns);
                DebtPayments = summary.DebtPayments;
                SupplierPayments = summary.SupplierPayments;
                DailyExpenses = summary.Expenses;

                NetSales = DailySales - DailyReturns;
                NetCashflow = DailySales + DebtPayments - DailyReturns - SupplierPayments - DailyExpenses;

                OnPropertyChanged(nameof(DailySales));
                OnPropertyChanged(nameof(DailyReturns));
                OnPropertyChanged(nameof(DebtPayments));
                OnPropertyChanged(nameof(SupplierPayments));
                OnPropertyChanged(nameof(DailyExpenses));
                OnPropertyChanged(nameof(NetSales));
                OnPropertyChanged(nameof(NetCashflow));
            }, "Updating financial summary");
        }
    }
}