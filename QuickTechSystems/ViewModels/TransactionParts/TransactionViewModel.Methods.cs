using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private void SubscribeToAllDetails()
        {
            if (CurrentTransaction?.Details == null) return;

            foreach (var detail in CurrentTransaction.Details)
            {
                SubscribeToDetailChanges(detail);
            }

            // Also subscribe to collection changes
            if (CurrentTransaction.Details is ObservableCollection<TransactionDetailDTO> observableDetails)
            {
                // Remove previous handler if exists to avoid duplicates
                observableDetails.CollectionChanged -= TransactionDetails_CollectionChanged;
                observableDetails.CollectionChanged += TransactionDetails_CollectionChanged;
            }
        }
        private Window GetOwnerWindow()
        {
            try
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting owner window: {ex.Message}");
                return null; // Return null instead of failing
            }
        }
        private void SubscribeToDetailChanges(TransactionDetailDTO detail)
        {
            if (detail != null)
            {
                // Unsubscribe first to avoid duplicate subscriptions
                detail.PropertyChanged -= TransactionDetail_PropertyChanged;
                detail.PropertyChanged += TransactionDetail_PropertyChanged;
            }
        }

        private void TransactionDetail_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TransactionDetailDTO.Quantity) ||
                e.PropertyName == nameof(TransactionDetailDTO.UnitPrice) ||
                e.PropertyName == nameof(TransactionDetailDTO.Discount) ||
                e.PropertyName == nameof(TransactionDetailDTO.Total))
            {
                // Update totals whenever a relevant property changes
                UpdateTotals();
            }
        }
        private void OpenNewTransactionWindow()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Opening new transaction window...");

                if (_transactionWindowManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: TransactionWindowManager is null");
                    WindowManager.ShowError("Transaction Window Manager is not available");
                    return;
                }

                _transactionWindowManager.OpenNewTransactionWindow();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening new transaction window: {ex.Message}");
                WindowManager.ShowError($"Failed to open new transaction window: {ex.Message}");
            }
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
                            OnPropertyChanged(nameof(LookupTransactionId));
                        });

                        Debug.WriteLine($"Latest transaction ID: {latestId}, Next transaction ID: {nextTransactionId}");
                    }
                    else
                    {
                        // If no transactions found, set to 1
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            LookupTransactionId = "1";
                            OnPropertyChanged(nameof(LookupTransactionId));
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
                        OnPropertyChanged(nameof(LookupTransactionId));
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

                // Clear any current transaction data first - use SafeClearTransaction to avoid UI issues
                await SafeClearTransactionAsync();

                // Explicitly clear customer information
                SelectedCustomer = null;
                CustomerSearchText = string.Empty;
                // Ensure dropdown is closed
                IsCustomerSearchVisible = false;

                // Ensure UI is updated
                OnPropertyChanged(nameof(SelectedCustomer));
                OnPropertyChanged(nameof(CustomerSearchText));
                OnPropertyChanged(nameof(IsCustomerSearchVisible));

                // Get the transaction by ID
                var transaction = await _transactionService.GetByIdAsync(transactionId);

                // If transaction doesn't exist, just show a message and don't throw an exception
                if (transaction == null)
                {
                    StatusMessage = $"Transaction #{transactionId} not found - Ready for new entry";
                    OnPropertyChanged(nameof(StatusMessage));
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
                SubscribeToAllDetails();
                // Populate customer information ONLY if available
                if (transaction.CustomerId.HasValue && transaction.CustomerId.Value > 0)
                {
                    try
                    {
                        var customer = await _customerService.GetByIdAsync(transaction.CustomerId.Value);
                        if (customer != null)
                        {
                            // Temporarily disable search trigger
                            _isNavigating = true;

                            // Set the customer directly without showing dropdown
                            SelectedCustomer = customer;
                            CustomerSearchText = customer.Name ?? "Unknown";

                            // Explicitly ensure dropdown is closed
                            IsCustomerSearchVisible = false;
                            OnPropertyChanged(nameof(IsCustomerSearchVisible));

                            // Re-enable search trigger after a short delay
                            await Task.Delay(100);
                            _isNavigating = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading customer for transaction: {ex.Message}");
                        // Continue without customer data
                    }
                }

                // Update totals
                UpdateTotals();

                // Set editing state
                IsEditingTransaction = true;
                OnPropertyChanged(nameof(IsEditingTransaction));
                OnPropertyChanged(nameof(CashPaymentButtonText));
                OnPropertyChanged(nameof(CustomerBalanceButtonText));
                OnPropertyChanged(nameof(EditModeIndicatorVisibility));

                // Update UI
                StatusMessage = $"Editing Transaction #{originalTransactionId}";
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(CurrentTransaction));

                // Log the action
                Debug.WriteLine($"Loaded Transaction #{originalTransactionId} for editing");
            }, "Looking up transaction", "Transaction loaded for editing");
        }

        // Helper method for safely clearing the transaction
        private async Task SafeClearTransactionAsync()
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (CurrentTransaction != null)
                    {
                        if (CurrentTransaction.Details != null)
                        {
                            CurrentTransaction.Details.Clear();
                        }

                        // Reset state
                        DiscountAmount = 0;
                    }

                    // Create a new transaction to ensure clean state
                    CurrentTransaction = new TransactionDTO
                    {
                        Details = new ObservableCollection<TransactionDetailDTO>(),
                        TransactionDate = DateTime.Now,
                        Status = TransactionStatus.Pending
                    };

                    UpdateTotals();

                    // Update UI
                    OnPropertyChanged(nameof(CurrentTransaction));
                    OnPropertyChanged(nameof(DiscountAmount));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error clearing transaction: {ex.Message}");
                }
            });
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
                    try
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating UI with customer search results: {ex.Message}");
                    }
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
                try
                {
                    MessageBox.Show(
                        "Invalid transaction number. Please enter a numeric value.",
                        "Invalid Input",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error showing alert: {ex.Message}");
                }
            });

            try
            {
                // Update status message
                StatusMessage = "Loading latest transaction number...";
                OnPropertyChanged(nameof(StatusMessage));

                // Reset to the latest transaction ID
                await LoadLatestTransactionIdAsync();

                // Update status message again
                StatusMessage = "Ready for new transaction";
                OnPropertyChanged(nameof(StatusMessage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resetting transaction ID: {ex.Message}");
                // Set a safe default
                LookupTransactionId = "1";
                StatusMessage = "Error loading transaction number";
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
                    // Store original transaction details for comparison
                    var originalTransaction = await _transactionService.GetByIdAsync(CurrentTransaction.TransactionId);
                    if (originalTransaction == null)
                    {
                        throw new InvalidOperationException($"Original transaction #{CurrentTransaction.TransactionId} not found");
                    }

                    // Store original values for later comparison
                    decimal originalAmount = originalTransaction.TotalAmount;
                    string originalPaymentMethod = originalTransaction.PaymentMethod;
                    bool isCustomerDebt = originalPaymentMethod == "CustomerDebt";
                    int? originalCustomerId = originalTransaction.CustomerId;

                    // Prepare the transaction for update
                    CurrentTransaction.TransactionDate = DateTime.Now;
                    CurrentTransaction.CustomerId = SelectedCustomer?.CustomerId;
                    CurrentTransaction.CustomerName = SelectedCustomer?.Name ?? "Walk-in Customer";
                    CurrentTransaction.TotalAmount = TotalAmount;
                    CurrentTransaction.Status = TransactionStatus.Completed;

                    // Preserve the original payment method
                    CurrentTransaction.PaymentMethod = originalPaymentMethod;

                    // Process the updated transaction
                    var updatedTransaction = await _transactionService.UpdateAsync(CurrentTransaction);

                    // Calculate the difference in amount
                    decimal amountDifference = TotalAmount - originalAmount;

                    // If this is a customer debt transaction, update the customer balance only by the difference
                    if (isCustomerDebt && Math.Abs(amountDifference) > 0.01m)
                    {
                        // Only update customer balance if customer ID matches
                        if (SelectedCustomer?.CustomerId == originalCustomerId && originalCustomerId.HasValue)
                        {
                            await _customerService.UpdateBalanceAsync(
                                originalCustomerId.Value,
                                amountDifference  // Only apply the difference, not the full amount
                            );

                            Debug.WriteLine($"Updated customer balance by difference: {amountDifference:C2}");
                        }
                    }
                    // Only update drawer for cash transactions
                    else if (originalPaymentMethod.ToLower() == "cash" && Math.Abs(amountDifference) > 0.01m)
                    {
                        await _drawerService.UpdateDrawerTransactionForModifiedSaleAsync(
                            CurrentTransaction.TransactionId,
                            originalAmount,
                            TotalAmount,
                            $"Modified Transaction #{CurrentTransaction.TransactionId}"
                        );
                    }

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
                    OnPropertyChanged(nameof(StatusMessage));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating transaction: {ex.Message}");
                    try
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine("Transaction rollback successful");
                    }
                    catch (Exception rollbackEx)
                    {
                        Debug.WriteLine($"Rollback error: {rollbackEx.Message}");
                        // Additional handling for failed rollbacks
                    }
                    throw new InvalidOperationException($"Failed to update transaction: {ex.Message}", ex);
                }
            }, "Updating transaction", "Transaction updated successfully");
        }

        private void FilterProductsByCategory(CategoryDTO category)
        {
            if (_allProducts == null)
            {
                FilteredProducts = new ObservableCollection<ProductDTO>();
                Debug.WriteLine("No products available to filter by category");
                return;
            }

            try
            {
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error filtering products by category: {ex.Message}");
                // Default to empty collection on error
                FilteredProducts = new ObservableCollection<ProductDTO>();
                OnPropertyChanged(nameof(FilteredProducts));
            }
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

                try
                {
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
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading restaurant mode preference: {ex.Message}");
                    // Default to standard mode on error
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsProductCardsVisible = false;
                        IsRestaurantMode = false;
                        OnPropertyChanged(nameof(IsProductCardsVisible));
                        OnPropertyChanged(nameof(IsRestaurantMode));
                    });
                }
            }, "Loading restaurant mode preference");
        }

        private void OnApplicationModeChanged(ApplicationModeChangedEvent evt)
        {
            try
            {
                // Update on UI thread
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in UI update during mode change: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling application mode change: {ex.Message}");
            }
        }

        public void AddProductToTransaction(ProductDTO product)
        {
            if (product == null) return;

            try
            {
                if (CurrentTransaction == null)
                {
                    StartNewTransaction();
                }

                // Check if product is already in the transaction
                var existingDetail = CurrentTransaction.Details?.FirstOrDefault(d =>
                    d.ProductId == product.ProductId);

                // Get price from customer-specific pricing if available
                decimal unitPrice = product.SalePrice;
                if (CustomerSpecificPrices.TryGetValue(product.ProductId, out decimal specialPrice))
                {
                    unitPrice = specialPrice;
                }

                if (existingDetail != null)
                {
                    // Update existing detail
                    decimal oldQuantity = existingDetail.Quantity;
                    existingDetail.Quantity += 1m; // Add exactly 1 unit

                    // Recalculate total with precise decimal arithmetic
                    existingDetail.Total = decimal.Multiply(existingDetail.Quantity, existingDetail.UnitPrice);

                    StatusMessage = $"Added 1 more {product.Name} (Total: {existingDetail.Quantity})";
                }
                else
                {
                    // Add new detail
                    var detail = new TransactionDetailDTO
                    {
                        ProductId = product.ProductId,
                        ProductName = product.Name,
                        ProductBarcode = product.Barcode,
                        CategoryId = product.CategoryId,
                        Quantity = 1m, // Start with exactly 1 unit
                        UnitPrice = unitPrice,
                        PurchasePrice = product.PurchasePrice,
                        Total = unitPrice // Total for 1 unit equals unit price
                    };

                    CurrentTransaction.Details.Add(detail);

                    // Subscribe to property changes for the new detail
                    SubscribeToDetailChanges(detail);

                    StatusMessage = $"Added {product.Name}";
                }

                // Update totals
                UpdateTotals();

                // Signal property changes
                OnPropertyChanged(nameof(CurrentTransaction));
                OnPropertyChanged(nameof(StatusMessage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding product to transaction: {ex.Message}");
                StatusMessage = $"Error adding product: {ex.Message}";
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
        public void AddProductToTransaction(ProductDTO product, decimal quantity)
        {
            AddProductToTransactionWithQuantity(product, quantity);
        }
        public void AddProductToTransaction(ProductDTO product, int quantity)
        {
            // Convert the int quantity to decimal and call our implementation method
            AddProductToTransactionWithQuantity(product, Convert.ToDecimal(quantity));
        }
        // Add this public method to TransactionViewModel class
        public void AddProductToTransactionWithQuantity(ProductDTO product, decimal quantity)
        {
            if (product == null) return;

            try
            {
                if (CurrentTransaction == null)
                {
                    StartNewTransaction();
                }

                // Check if product is already in the transaction
                var existingDetail = CurrentTransaction.Details?.FirstOrDefault(d =>
                    d.ProductId == product.ProductId);

                // Get price from customer-specific pricing if available
                decimal unitPrice = product.SalePrice;
                if (CustomerSpecificPrices.TryGetValue(product.ProductId, out decimal specialPrice))
                {
                    unitPrice = specialPrice;
                }

                if (existingDetail != null)
                {
                    // Update existing detail
                    decimal oldQuantity = existingDetail.Quantity;
                    existingDetail.Quantity += quantity; // Add specified quantity

                    // Recalculate total with precise decimal arithmetic
                    existingDetail.Total = decimal.Multiply(existingDetail.Quantity, existingDetail.UnitPrice);

                    StatusMessage = $"Added {quantity} more {product.Name} (Total: {existingDetail.Quantity})";
                }
                else
                {
                    // Add new detail
                    var detail = new TransactionDetailDTO
                    {
                        ProductId = product.ProductId,
                        ProductName = product.Name,
                        ProductBarcode = product.Barcode,
                        CategoryId = product.CategoryId,
                        Quantity = quantity, // Use specified quantity
                        UnitPrice = unitPrice,
                        PurchasePrice = product.PurchasePrice,
                        Total = decimal.Multiply(quantity, unitPrice) // Calculate total with precise decimal math
                    };

                    CurrentTransaction.Details.Add(detail);
                    StatusMessage = $"Added {quantity} {product.Name}";
                }

                // Update totals
                UpdateTotals();

                // Signal property changes
                OnPropertyChanged(nameof(CurrentTransaction));
                OnPropertyChanged(nameof(StatusMessage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding product to transaction: {ex.Message}");
                StatusMessage = $"Error adding product: {ex.Message}";
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
        // File: QuickTechSystems/ViewModels/TransactionParts/TransactionViewModel.Methods.cs
        private async void CheckAndAlertLowStock(ProductDTO product, int quantity)
        {
            // Only check for active products
            if (product == null || !product.IsActive) return;

            try
            {
                // Calculate the potential new stock level after this transaction
                int potentialNewStock = product.CurrentStock - quantity;

                // Check if the potential new stock is at or below the minimum stock level
                if (potentialNewStock <= product.MinimumStock)
                {
                    // COMMENTED OUT: Display simplified alert to the user
                    /* 
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show(
                            $"Alert: Product '{product.Name}' is reaching low stock level.",
                            "Low Stock Alert",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning)
                    );
                    */

                    // Still log the low stock event in the database
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
                                    product.Name ?? "Unknown Product",
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking stock levels: {ex.Message}");
                // Continue with transaction - don't block the sale
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

                try
                {
                    await Task.Run(() => {
                        foreach (var product in batch)
                        {
                            try
                            {
                                if (product?.Image == null) continue;

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
                                Debug.WriteLine($"Error caching image for product {product?.ProductId}: {ex.Message}");
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing image batch: {ex.Message}");
                }

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
                try
                {
                    const string userId = "default";
                    await _systemPreferencesService.SavePreferenceAsync(userId, "RestaurantMode", IsRestaurantMode.ToString());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error saving restaurant mode preference: {ex.Message}");
                    // Continue even if saving preference fails
                }

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
            bool semaphoreAcquired = false;

            try
            {
                // Wait for any previous operations to complete with timeout
                semaphoreAcquired = await _categoryLoadSemaphore.WaitAsync(TimeSpan.FromSeconds(5));
                if (!semaphoreAcquired)
                {
                    Debug.WriteLine("Timed out waiting to load categories");
                    return;
                }

                Debug.WriteLine("Loading product categories...");

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
                    try
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error setting categories in UI: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading product categories: {ex.Message}");
                await ShowErrorMessageAsync($"Error loading product categories: {ex.Message}");
            }
            finally
            {
                // Always release the semaphore if we acquired it
                if (semaphoreAcquired)
                {
                    _categoryLoadSemaphore.Release();
                }
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

        private async Task CloseDrawerAsync()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                // Check if there's an ongoing transaction
                if (CurrentTransaction?.Details?.Any() == true)
                {
                    var transactionConfirmResult = await WindowManager.InvokeAsync(() => MessageBox.Show(
                        "You have an ongoing transaction. Closing the drawer will cancel this transaction. Continue?",
                        "Unsaved Transaction",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning));

                    if (transactionConfirmResult != MessageBoxResult.Yes)
                        return;
                }

                // Get current drawer balance
                var drawer = await _drawerService.GetCurrentDrawerAsync();
                if (drawer == null)
                {
                    throw new InvalidOperationException("No active drawer found");
                }

                decimal currentBalance = drawer.CurrentBalance;

                // Show confirmation with current balance
                var drawerCloseConfirmResult = await WindowManager.InvokeAsync(() => MessageBox.Show(
                    $"Current cash in drawer: {currentBalance:C2}\n\nAre you sure you want to close the drawer? This will end your current session.",
                    "Confirm Drawer Close",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question));

                if (drawerCloseConfirmResult != MessageBoxResult.Yes)
                    return;

                var ownerWindow = GetOwnerWindow();
                if (ownerWindow == null)
                {
                    throw new InvalidOperationException("Cannot find a valid window to show the dialog");
                }

                // Process drawer closing with current balance
                await _drawerService.CloseDrawerAsync(currentBalance, "Closed by user at end of shift");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        MessageBox.Show(
                            ownerWindow,
                            "Drawer closed successfully. The application will now exit.",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error showing close success message: {ex.Message}");
                    }
                });

                // Close the application after showing the success message
                System.Windows.Application.Current.Shutdown();
            }, "Closing drawer");
        }

        public void StartNewTransaction()
        {
            try
            {
                // Get current user information
                var currentUser = App.Current.Properties["CurrentUser"] as EmployeeDTO;

                // Generate a new unique transaction ID based on current timestamp
                CurrentTransactionNumber = DateTime.Now.ToString("yyyyMMddHHmmss");

                // Reset customer selection
                SelectedCustomer = null;
                CustomerSearchText = string.Empty;
                IsCustomerSearchVisible = false;

                // Reset product selection
                ProductSearchText = string.Empty;
                IsProductSearchVisible = false;

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

                // Reset financial values
                DiscountAmount = 0;
                ClearTotals();

                // Reset any cached product or customer data
                CustomerSpecificPrices = new Dictionary<int, decimal>();

                // Update status message
                StatusMessage = "New transaction started";

                // Notify UI of changes to ensure everything updates properly
                OnPropertyChanged(nameof(IsEditingTransaction));
                OnPropertyChanged(nameof(CashPaymentButtonText));
                OnPropertyChanged(nameof(CustomerBalanceButtonText));
                OnPropertyChanged(nameof(EditModeIndicatorVisibility));
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(CurrentTransaction));
                OnPropertyChanged(nameof(CurrentTransactionNumber));
                OnPropertyChanged(nameof(CashierName));
                OnPropertyChanged(nameof(SelectedCustomer));
                OnPropertyChanged(nameof(CustomerSearchText));
                OnPropertyChanged(nameof(IsCustomerSearchVisible));
                if (CurrentTransaction.Details is ObservableCollection<TransactionDetailDTO> observableDetails)
                {
                    observableDetails.CollectionChanged += TransactionDetails_CollectionChanged;
                }
            }


            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting new transaction: {ex.Message}");
                // Create a minimal transaction to prevent null reference errors
                CurrentTransaction = new TransactionDTO
                {
                    TransactionDate = DateTime.Now,
                    Status = TransactionStatus.Pending,
                    Details = new ObservableCollection<TransactionDetailDTO>(),
                };
            }
        }


        private void TransactionDetails_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Handle added items
            if (e.NewItems != null)
            {
                foreach (TransactionDetailDTO detail in e.NewItems)
                {
                    SubscribeToDetailChanges(detail);
                }
            }

            // Handle removed items
            if (e.OldItems != null)
            {
                foreach (TransactionDetailDTO detail in e.OldItems)
                {
                    detail.PropertyChanged -= TransactionDetail_PropertyChanged;
                }
            }

            // Update totals whenever the collection changes
            UpdateTotals();
        }
        private async Task HoldTransaction()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (CurrentTransaction?.Details.Any() == true)
                {
                    CurrentTransaction.Status = TransactionStatus.Pending;

                    // Create a deep copy of the transaction before holding
                    var transactionCopy = new TransactionDTO
                    {
                        TransactionId = CurrentTransaction.TransactionId,
                        TransactionDate = CurrentTransaction.TransactionDate,
                        CustomerId = CurrentTransaction.CustomerId,
                        CustomerName = CurrentTransaction.CustomerName,
                        TotalAmount = CurrentTransaction.TotalAmount,
                        PaidAmount = CurrentTransaction.PaidAmount,
                        TransactionType = CurrentTransaction.TransactionType,
                        Status = TransactionStatus.Pending,
                        PaymentMethod = CurrentTransaction.PaymentMethod,
                        CashierId = CurrentTransaction.CashierId,
                        CashierName = CurrentTransaction.CashierName,
                        Details = new ObservableCollection<TransactionDetailDTO>(
                            CurrentTransaction.Details.Select(d => new TransactionDetailDTO
                            {
                                ProductId = d.ProductId,
                                ProductName = d.ProductName,
                                ProductBarcode = d.ProductBarcode,
                                Quantity = d.Quantity,
                                UnitPrice = d.UnitPrice,
                                PurchasePrice = d.PurchasePrice,
                                Discount = d.Discount,
                                Total = d.Total
                            }))
                    };

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        HeldTransactions.Add(transactionCopy);
                        OnPropertyChanged(nameof(HeldTransactions));
                    });

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
                if (result != null)
                {
                    await WindowManager.InvokeAsync(() => MessageBox.Show(
                        $"Quote #{result.QuoteNumber} created successfully",
                        "Quote Saved",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));

                    StartNewTransaction();
                }
                else
                {
                    throw new InvalidOperationException("Failed to create quote. Please try again.");
                }
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
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            CurrentTransaction = heldTransaction;
                            HeldTransactions.Remove(heldTransaction);
                            OnPropertyChanged(nameof(HeldTransactions));
                            OnPropertyChanged(nameof(CurrentTransaction));

                            // Also update customer info if available
                            if (heldTransaction.CustomerId.HasValue && heldTransaction.CustomerId.Value > 0)
                            {
                                _customerService.GetByIdAsync(heldTransaction.CustomerId.Value)
                                    .ContinueWith(t => {
                                        if (t.IsCompleted && !t.IsFaulted && t.Result != null)
                                        {
                                            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                                                _isNavigating = true;
                                                SelectedCustomer = t.Result;
                                                CustomerSearchText = t.Result.Name;
                                                _isNavigating = false;
                                            });
                                        }
                                    });
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error recalling transaction: {ex.Message}");
                            throw;
                        }
                    });

                    UpdateTotals();
                    StatusMessage = "Transaction recalled";
                    OnPropertyChanged(nameof(StatusMessage));
                }
                else
                {
                    throw new InvalidOperationException("No held transactions to recall");
                }
            }, "Recalling transaction");
        }

        private async Task VoidTransaction()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (CurrentTransaction == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No transaction to void");
                }

                var result = await WindowManager.InvokeAsync(() => MessageBox.Show(
                    "Are you sure you want to void this transaction?",
                    "Confirm Void",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question));

                if (result != MessageBoxResult.Yes)
                    return;

                if (CurrentTransaction.TransactionId > 0)
                {
                    // This is an existing transaction that needs to be voided in the database
                    await _transactionService.UpdateStatusAsync(CurrentTransaction.TransactionId, TransactionStatus.Cancelled);

                    // Publish event to notify other components
                    _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>("Update",
                        new TransactionDTO
                        {
                            TransactionId = CurrentTransaction.TransactionId,
                            Status = TransactionStatus.Cancelled
                        }));
                }

                StartNewTransaction();
                StatusMessage = "Transaction voided";
                OnPropertyChanged(nameof(StatusMessage));
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

                // Update the prices dictionary safely on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                    CustomerSpecificPrices = newPrices;
                });

                // Update prices for existing items in cart
                if (CurrentTransaction?.Details != null)
                {
                    bool pricesChanged = false;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        foreach (var detail in CurrentTransaction.Details)
                        {
                            pricesChanged |= UpdateProductPrice(detail);
                        }
                    });

                    if (pricesChanged)
                    {
                        UpdateTotals();

                        // Notify UI of updated items
                        OnPropertyChanged(nameof(CurrentTransaction.Details));
                    }
                }

                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading customer prices: {ex.Message}");
                StatusMessage = "Error loading customer prices";
                CustomerSpecificPrices = new Dictionary<int, decimal>();
                throw;
            }
        }

        private bool UpdateProductPrice(TransactionDetailDTO detail)
        {
            if (detail == null) return false;

            if (_customerSpecificPrices.TryGetValue(detail.ProductId, out decimal customPrice))
            {
                if (Math.Abs(detail.UnitPrice - customPrice) > 0.001m) // Use small epsilon for decimal comparison
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
                try
                {
                    dialog.Owner = GetOwnerWindow();
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    return dialog.ShowDialog();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error showing dialog: {ex.Message}");
                    throw;
                }
            });
        }

        private async Task<string> ShowInputDialog(string prompt, string title)
        {
            try
            {
                return await WindowManager.InvokeAsync(() =>
                {
                    var dialog = new InputDialog(title, prompt)
                    {
                        Owner = GetOwnerWindow() ?? System.Windows.Application.Current.MainWindow
                    };

                    return dialog.ShowDialog() == true ? dialog.Input : string.Empty;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing input dialog: {ex.Message}");
                return string.Empty;
            }
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            await WindowManager.InvokeAsync(() =>
            {
                try
                {
                    var ownerWindow = GetOwnerWindow();
                    MessageBox.Show(
                        ownerWindow ?? System.Windows.Application.Current.MainWindow,
                        message,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error showing error message: {ex.Message}");
                    // Last resort - write to console if even showing the error fails
                    Console.WriteLine($"CRITICAL ERROR: {message}");
                }
            });
        }

        private async Task UpdateFinancialSummaryAsync()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                var startDate = DateTime.Today;
                var endDate = DateTime.Now;

                try
                {
                    var summary = await _drawerService.GetFinancialSummaryAsync(startDate, endDate);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
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
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting financial summary: {ex.Message}");
                    // Set default values on error
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        DailySales = 0;
                        SupplierPayments = 0;
                        DailyExpenses = 0;
                        NetSales = 0;
                        NetCashflow = 0;

                        OnPropertyChanged(nameof(DailySales));
                        OnPropertyChanged(nameof(SupplierPayments));
                        OnPropertyChanged(nameof(DailyExpenses));
                        OnPropertyChanged(nameof(NetSales));
                        OnPropertyChanged(nameof(NetCashflow));
                    });
                }
            }, "Updating financial summary");
        }

        private void IncrementTransactionId()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LookupTransactionId))
                {
                    LookupTransactionId = "1";
                    return;
                }

                if (int.TryParse(LookupTransactionId, out int currentId))
                {
                    LookupTransactionId = (currentId + 1).ToString();

                    // Automatically trigger lookup
                    LookupTransactionAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error incrementing transaction ID: {ex.Message}");
            }
        }

        private void DecrementTransactionId()
        {
            try
            {
                if (int.TryParse(LookupTransactionId, out int currentId) && currentId > 1)
                {
                    LookupTransactionId = (currentId - 1).ToString();

                    // Automatically trigger lookup
                    LookupTransactionAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error decrementing transaction ID: {ex.Message}");
            }
        }

        private async Task InitializeProductsAsync()
        {
            try
            {
                var products = await _productService.GetAllAsync();

                // Filter to only include active products
                var activeProducts = products.Where(p => p.IsActive).ToList();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                    AllProducts = new ObservableCollection<ProductDTO>(activeProducts);
                    OnPropertyChanged(nameof(AllProducts));

                    // Initialize FilteredProducts with only Internet category active products
                    var internetProducts = activeProducts
                        .Where(p => p.CategoryName?.Contains("Internet", StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();
                    FilteredProducts = new ObservableCollection<ProductDTO>(internetProducts);
                    OnPropertyChanged(nameof(FilteredProducts));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading products: {ex.Message}");
                await ShowErrorMessageAsync("Error loading products. Some features may not work correctly.");
            }
        }

        private void FilterProductsForDropdown(string searchText)
        {
            try
            {
                if (_allProducts == null)
                {
                    FilteredProducts = new ObservableCollection<ProductDTO>();
                    Debug.WriteLine("No products available to filter");
                    OnPropertyChanged(nameof(FilteredProducts));
                    return;
                }

                // Always start with only active products
                var filteredList = _allProducts.Where(p => p.IsActive).ToList();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    filteredList = filteredList.Where(p =>
                        (p.Name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true) ||
                        (p.Barcode?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true))
                        .ToList();
                }

                FilteredProducts = new ObservableCollection<ProductDTO>(filteredList);
                OnPropertyChanged(nameof(FilteredProducts));
                Debug.WriteLine($"Filtered products count: {FilteredProducts.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error filtering products: {ex.Message}");
                FilteredProducts = new ObservableCollection<ProductDTO>();
                OnPropertyChanged(nameof(FilteredProducts));
            }
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