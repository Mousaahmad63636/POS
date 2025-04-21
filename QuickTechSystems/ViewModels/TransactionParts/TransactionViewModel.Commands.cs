using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore.Storage;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Enums;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.Views.Dialogs;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel
    {

        public ICommand ProcessBarcodeCommand { get; private set; }
        public ICommand? SearchProductsCommand { get; private set; }
        public ICommand? HoldTransactionCommand { get; private set; }
        public ICommand? RecallTransactionCommand { get; private set; }
        public ICommand? VoidTransactionCommand { get; private set; }
        public ICommand? NewCustomerCommand { get; private set; }
        public ICommand? RemoveItemCommand { get; private set; }
        public ICommand? VoidLastItemCommand { get; private set; }
        public ICommand? PriceCheckCommand { get; private set; }
        public ICommand? ChangeQuantityCommand { get; private set; }
        public ICommand? AddDiscountCommand { get; private set; }
        public ICommand? ReprintLastCommand { get; private set; }
        public ICommand? ClearTransactionCommand { get; private set; }
        public ICommand? CancelTransactionCommand { get; private set; }
        public ICommand? CashPaymentCommand { get; private set; }
        public ICommand? PrintReceiptCommand { get; private set; }
        public ICommand AddToCustomerBalanceCommand { get; private set; }
        public ICommand CloseDrawerCommand { get; private set; }

        public ICommand ChangePriceCommand { get; private set; }
        public ICommand SaveAsQuoteCommand { get; private set; }
        public ICommand SelectCategoryCommand { get; private set; }
        public ICommand ClearCustomerCommand { get; private set; }
        public ICommand LookupTransactionCommand { get; private set; }
        public ICommand IncrementTransactionIdCommand { get; private set; }
        public ICommand DecrementTransactionIdCommand { get; private set; }

        private void InitializeCommands()
        {
            SelectTableCommand = new AsyncRelayCommand(async _ => await ShowTableSelectionDialog());
            SwitchTableCommand = new AsyncRelayCommand(async _ => await SwitchTableAsync());
            IncrementTransactionIdCommand = new RelayCommand(_ => IncrementTransactionId());
            DecrementTransactionIdCommand = new RelayCommand(_ => DecrementTransactionId());
            // Existing commands
            ProcessBarcodeCommand = new AsyncRelayCommand(async _ => await ProcessBarcodeInput());

            // Transaction management commands with conditions
            HoldTransactionCommand = new AsyncRelayCommand(
                async _ => await HoldTransaction(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            RecallTransactionCommand = new AsyncRelayCommand(
                async _ => await RecallTransaction(),
                _ => HeldTransactions != null && HeldTransactions.Any());

            ChangePriceCommand = new AsyncRelayCommand(
    async _ => await ChangePrice(),
    _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            VoidTransactionCommand = new AsyncRelayCommand(
                async _ => await VoidTransaction(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            ClearTransactionCommand = new RelayCommand(
                _ => ClearTransaction(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            CancelTransactionCommand = new RelayCommand(
                _ => CancelTransaction(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());
            NewTransactionWindowCommand = new RelayCommand(_ =>
            {
                try
                {
                    _transactionWindowManager.OpenNewTransactionWindow();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error opening new transaction window: {ex.Message}");
                    WindowManager.ShowError("Failed to open new transaction window. Please try again.");
                }
            });

            LookupTransactionCommand = new AsyncRelayCommand(
                async _ => await LookupTransactionAsync(),
                _ => !string.IsNullOrWhiteSpace(LookupTransactionId));

            AddToCustomerBalanceCommand = new AsyncRelayCommand(
     async _ =>
     {

         if (IsEditingTransaction && CurrentTransaction?.TransactionId > 0)
         {

             await UpdateExistingTransactionAsync();
         }
         else
         {

             await ProcessAsCustomerDebt();
         }
     },
     _ => CurrentTransaction?.Details != null &&
          CurrentTransaction.Details.Any() &&
          SelectedCustomer != null);

            // Item management commands with conditions
            RemoveItemCommand = new RelayCommand(
                RemoveItem,
                parameter => CurrentTransaction?.Details != null &&
                             CurrentTransaction.Details.Any() &&
                             parameter is TransactionDetailDTO);

            VoidLastItemCommand = new RelayCommand(
                _ => VoidLastItem(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            ChangeQuantityCommand = new AsyncRelayCommand(
                async _ => await ChangeItemQuantityAsync(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            AddDiscountCommand = new RelayCommand(
                _ => AddDiscount(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            PriceCheckCommand = new AsyncRelayCommand(async _ => await CheckPrice());

            SaveAsQuoteCommand = new AsyncRelayCommand(
                async _ => await SaveAsQuoteAsync(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any() && SelectedCustomer != null);

            // Customer commands
            NewCustomerCommand = new AsyncRelayCommand(async _ => await ShowNewCustomerDialog());

            // Payment commands
            CashPaymentCommand = new AsyncRelayCommand(
      async _ =>
      {
          // Validate if there are products in the transaction
          if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
          {
              await ShowErrorMessageAsync("You must select a product before completing the sale.");
              return;
          }

          // Check if this is an existing transaction that's being edited
          if (IsEditingTransaction && CurrentTransaction.TransactionId > 0)
          {
              // Update existing transaction, preserving its original payment method
              await UpdateExistingTransactionAsync();
          }
          else
          {
              // Create new cash transaction
              await ProcessCashPayment();
          }
      },
      _ => CurrentTransaction != null && CurrentTransaction.Details != null && CurrentTransaction.Details.Any());

            ToggleViewCommand = new RelayCommand(_ => ToggleView());

            AddToCartCommand = new RelayCommand<ProductDTO>(product =>
            {
                if (product != null)
                    AddProductToTransaction(product);
                else
                    WindowManager.ShowWarning("Please select a product first.");
            });

            ReprintLastCommand = new AsyncRelayCommand(async _ => await ReprintLast());

            PrintReceiptCommand = new AsyncRelayCommand(
                async _ => await PrintReceipt(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            SearchProductsCommand = new RelayCommand(_ =>
            {
                IsProductSearchVisible = true;
                SearchProducts();
            });

            SelectCategoryCommand = new RelayCommand<CategoryDTO>(category =>
            {
                SelectedCategory = category;
            });

            CloseDrawerCommand = new AsyncRelayCommand(async _ => await CloseDrawerAsync());
            ClearCustomerCommand = new RelayCommand(_ => ClearCustomerSelection());
        }


        private async Task ChangePrice()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No items in transaction");
                }

                // Get the selected item or the last item added if none is selected
                var selectedDetail = CurrentTransaction.Details.FirstOrDefault(d => d.IsSelected);
                if (selectedDetail == null)
                {
                    // If no item is selected, use the last item
                    selectedDetail = CurrentTransaction.Details.LastOrDefault();
                    if (selectedDetail == null)
                    {
                        throw new InvalidOperationException("No item selected");
                    }
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var dialog = new PriceDialog(selectedDetail.ProductName, selectedDetail.UnitPrice)
                    {
                        Owner = System.Windows.Application.Current.MainWindow
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        // Validate new price
                        if (dialog.NewPrice <= 0)
                        {
                            MessageBox.Show(
                                "Price must be a positive number.",
                                "Invalid Price",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        // Only allow price increases
                        if (dialog.NewPrice < selectedDetail.UnitPrice)
                        {
                            MessageBox.Show(
                                "New price cannot be lower than current price.",
                                "Invalid Price",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        // Update price and recalculate total
                        selectedDetail.UnitPrice = dialog.NewPrice;
                        selectedDetail.Total = selectedDetail.Quantity * selectedDetail.UnitPrice;

                        // Force UI refresh
                        var index = CurrentTransaction.Details.IndexOf(selectedDetail);
                        if (index >= 0)
                        {
                            CurrentTransaction.Details.RemoveAt(index);
                            CurrentTransaction.Details.Insert(index, selectedDetail);
                        }

                        // Update totals and notify UI
                        UpdateTotals();
                        OnPropertyChanged(nameof(CurrentTransaction.Details));
                        OnPropertyChanged(nameof(CurrentTransaction));
                    }
                });
            }, "Changing price");
        }

        private async Task ProcessAsCustomerDebt()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                // Validate customer is selected
                if (SelectedCustomer == null)
                {
                    throw new InvalidOperationException("Please select a customer before adding to their balance.");
                }

                // Validate active transaction
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No items in transaction to add to customer balance.");
                }

                // No confirmation dialog - proceed directly

                IDbContextTransaction dbTransaction = null;
                TransactionDTO transactionResult = null;

                try
                {
                    dbTransaction = await _unitOfWork.BeginTransactionAsync();

                    var currentUser = App.Current.Properties["CurrentUser"] as EmployeeDTO;
                    var transactionToProcess = new TransactionDTO
                    {
                        TransactionDate = DateTime.Now,
                        CustomerId = SelectedCustomer.CustomerId,
                        CustomerName = SelectedCustomer.Name,
                        TotalAmount = TotalAmount,
                        PaidAmount = 0, // No payment received yet
                        TransactionType = TransactionType.Sale,
                        Status = TransactionStatus.Completed,
                        PaymentMethod = "CustomerDebt", // Special payment method for debt
                        CashierId = currentUser?.EmployeeId.ToString() ?? "0",
                        CashierName = currentUser?.FullName ?? "Unknown",
                        Details = new ObservableCollection<TransactionDetailDTO>(CurrentTransaction.Details.Select(d =>
                            new TransactionDetailDTO
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

                    // Process the transaction
                    transactionResult = await _transactionService.ProcessSaleAsync(transactionToProcess);

                    // Update customer balance
                    await _customerService.UpdateBalanceAsync(
                        SelectedCustomer.CustomerId,
                        TotalAmount
                    );

                    // No drawer update needed for debt transactions

                    // Publish transaction event
                    _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>(
                        "Create",
                        transactionResult
                    ));

                    await dbTransaction.CommitAsync();

                    // Store the current transaction for printing
                    CurrentTransaction = transactionResult;

                    // Print receipt after successful transaction
                    try
                    {
                        await PrintReceipt();
                    }
                    catch (Exception printEx)
                    {
                        Debug.WriteLine($"Error printing receipt: {printEx.Message}");
                        await WindowManager.InvokeAsync(() =>
                            MessageBox.Show(
                                "Transaction completed successfully but there was an error printing the receipt.",
                                "Print Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning)
                        );
                        // Don't fail the whole transaction for a print error
                    }

                    // Start new transaction without showing success message
                    StartNewTransaction(false); // Pass false to prevent table selection dialog

                    // Update the lookup transaction ID with the next transaction number
                    await UpdateLookupTransactionIdAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Debt transaction error: {ex.Message}");

                    // Roll back if transaction hasn't been committed
                    if (dbTransaction != null)
                    {
                        try
                        {
                            await dbTransaction.RollbackAsync();
                        }
                        catch (Exception rollbackEx)
                        {
                            Debug.WriteLine($"Error during rollback: {rollbackEx.Message}");
                        }
                    }
                    throw;
                }
            }, "Processing customer debt transaction");
        }

        private void ClearCustomerSelection()
        {
            SelectedCustomer = null;
            CustomerSearchText = string.Empty;
            IsCustomerSearchVisible = false;
            IsSearchMessageVisible = false;

            OnPropertyChanged(nameof(SelectedCustomer));
            OnPropertyChanged(nameof(CustomerSearchText));
            OnPropertyChanged(nameof(IsCustomerSearchVisible));
            OnPropertyChanged(nameof(IsSearchMessageVisible));
        }

        private string GetCurrentUserId()
        {
            var currentUser = App.Current.Properties["CurrentUser"] as EmployeeDTO;
            return currentUser?.EmployeeId.ToString() ?? "0";
        }

        private string GetCurrentUserName()
        {
            var currentUser = App.Current.Properties["CurrentUser"] as EmployeeDTO;
            return currentUser?.FullName ?? "Unknown";
        }

        private void RemoveItem(object? parameter)
        {
            if (parameter is not TransactionDetailDTO detail || CurrentTransaction?.Details == null)
            {
                WindowManager.ShowWarning("No item selected to remove");
                return;
            }

            try
            {
                CurrentTransaction.Details.Remove(detail);
                UpdateTotals();

                // Notify UI of changes
                OnPropertyChanged(nameof(CurrentTransaction.Details));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing item: {ex.Message}");
                WindowManager.ShowError($"Error removing item: {ex.Message}");
            }
        }
        private async Task LoadRestaurantTablesAsync()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                var tables = await _restaurantTableService.GetActiveTablesAsync();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AvailableTables.Clear();
                    foreach (var table in tables)
                    {
                        AvailableTables.Add(table);
                    }

                    OnPropertyChanged(nameof(AvailableTables));
                });
            }, "Loading restaurant tables");
        }

        private async Task ShowTableSelectionDialog()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                // First, save the current transaction details if we have a selected table
                if (SelectedTable != null)
                {
                    SaveCurrentTableTransaction();
                }

                // Load tables if not already loaded
                if (AvailableTables.Count == 0)
                {
                    await LoadRestaurantTablesAsync();
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new TableSelectionDialog(AvailableTables);
                    dialog.Owner = GetOwnerWindow();

                    dialog.TableSelected += (sender, table) =>
                    {
                        // If switching to a different table, handle the transaction change
                        if (SelectedTable == null || SelectedTable.Id != table.Id)
                        {
                            // Set the new selected table
                            SelectedTable = table;

                            // Load transaction for the selected table
                            LoadTableTransaction(table.Id);

                            // Update totals just in case
                            UpdateTotals();

                            StatusMessage = $"Switched to Table {table.TableNumber}";
                        }
                    };

                    dialog.NewTransactionRequested += (sender, args) =>
                    {
                        // Create a new empty transaction without table
                        SelectedTable = null;
                        StartNewTransaction(true);
                    };

                    dialog.ShowDialog();
                });
            }, "Showing table selection dialog");
        }

        // Save the current transaction details for the current table
        private void SaveCurrentTableTransaction()
        {
            // Check if there's a valid table and transaction
            if (SelectedTable != null && CurrentTransaction?.Details != null)
            {
                // Create a new state object to hold all transaction data
                var state = new TableTransactionState
                {
                    SelectedCustomer = SelectedCustomer,
                    CustomerSearchText = CustomerSearchText,
                    DiscountAmount = DiscountAmount,
                    SubTotal = SubTotal,
                    TaxAmount = TaxAmount,
                    TotalAmount = TotalAmount,
                    ItemCount = ItemCount,
                    IsEditingTransaction = IsEditingTransaction
                };

                // Copy all transaction details
                state.CopyDetailsFrom(CurrentTransaction.Details);

                // Store in dictionary using table ID as key
                _tableTransactions[SelectedTable.Id] = state;

                // Update table status based on whether there are items
                bool hasItems = state.Details.Count > 0;
                if (hasItems && SelectedTable.Status != "Occupied")
                {
                    SelectedTable.Status = "Occupied";
                    _ = _restaurantTableService.UpdateTableStatusAsync(SelectedTable.Id, "Occupied");

                    // Update in our local collection
                    var tableInCollection = AvailableTables.FirstOrDefault(t => t.Id == SelectedTable.Id);
                    if (tableInCollection != null)
                    {
                        tableInCollection.Status = "Occupied";
                        OnPropertyChanged(nameof(AvailableTables));
                    }
                }
                else if (!hasItems && SelectedTable.Status == "Occupied")
                {
                    SelectedTable.Status = "Available";
                    _ = _restaurantTableService.UpdateTableStatusAsync(SelectedTable.Id, "Available");

                    // Update in our local collection
                    var tableInCollection = AvailableTables.FirstOrDefault(t => t.Id == SelectedTable.Id);
                    if (tableInCollection != null)
                    {
                        tableInCollection.Status = "Available";
                        OnPropertyChanged(nameof(AvailableTables));
                    }
                }
            }
        }

        private async Task SwitchTableAsync()
        {
            // Save current transaction for current table
            SaveCurrentTableTransaction();

            // Show table selection dialog
            await ShowTableSelectionDialog();
        }

        // Load transaction details for the specified table
        private void LoadTableTransaction(int tableId)
        {
            // Create an empty transaction first
            StartNewTransaction(false); // false means don't show table selection dialog

            // If we have saved details for this table, load them
            if (_tableTransactions.TryGetValue(tableId, out var state))
            {
                // Restore transaction details
                CurrentTransaction.Details.Clear();
                foreach (var detail in state.Details)
                {
                    CurrentTransaction.Details.Add(detail);
                }

                // Restore customer information
                SelectedCustomer = state.SelectedCustomer;
                CustomerSearchText = state.CustomerSearchText;

                // Restore financial values
                DiscountAmount = state.DiscountAmount;
                SubTotal = state.SubTotal;
                TaxAmount = state.TaxAmount;
                TotalAmount = state.TotalAmount;
                ItemCount = state.ItemCount;

                // Restore transaction state
                IsEditingTransaction = state.IsEditingTransaction;

                // Subscribe to property changes for all details
                SubscribeToAllDetails();

                // Notify UI of all the changes
                OnPropertyChanged(nameof(SelectedCustomer));
                OnPropertyChanged(nameof(CustomerSearchText));
                OnPropertyChanged(nameof(DiscountAmount));
                OnPropertyChanged(nameof(SubTotal));
                OnPropertyChanged(nameof(TaxAmount));
                OnPropertyChanged(nameof(TotalAmount));
                OnPropertyChanged(nameof(ItemCount));
                OnPropertyChanged(nameof(IsEditingTransaction));
                OnPropertyChanged(nameof(CurrentTransaction.Details));
            }
        }
        private void VoidLastItem()
        {
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                WindowManager.ShowWarning("No items in transaction to void");
                return;
            }

            var lastItem = CurrentTransaction.Details.LastOrDefault();
            if (lastItem != null)
            {
                CurrentTransaction.Details.Remove(lastItem);
                UpdateTotals();

                // Notify UI of changes
                OnPropertyChanged(nameof(CurrentTransaction.Details));
                OnPropertyChanged(nameof(CurrentTransaction));
            }
        }

        private void ClearTransaction()
        {
            // Skip the warning check if there are no items - simply return silently
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                return; // Return silently instead of showing warning
            }

            try
            {
                // Confirm with the user
                var result = MessageBox.Show(
                    "Are you sure you want to clear all items from this transaction?",
                    "Confirm Clear",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Reset the current transaction
                    if (CurrentTransaction.Details != null)
                    {
                        CurrentTransaction.Details.Clear();
                    }

                    // Reset discount and update totals
                    DiscountAmount = 0;
                    UpdateTotals();

                    // Reset customer if necessary
                    if (IsEditingTransaction == false)
                    {
                        SelectedCustomer = null;
                        CustomerSearchText = string.Empty;
                    }

                    // Reset transaction status message
                    StatusMessage = "Transaction cleared - Ready for new items";

                    // If in restaurant mode and we have a selected table, update its state
                    if (IsRestaurantMode && SelectedTable != null)
                    {
                        // Update the table transaction state
                        if (_tableTransactions.TryGetValue(SelectedTable.Id, out var state))
                        {
                            state.Details.Clear();
                            state.DiscountAmount = 0;
                            state.SubTotal = 0;
                            state.TaxAmount = 0;
                            state.TotalAmount = 0;
                            state.ItemCount = 0;
                            state.SelectedCustomer = null;
                            state.CustomerSearchText = string.Empty;
                        }

                        // Update table status
                        SelectedTable.Status = "Available";
                        _ = _restaurantTableService.UpdateTableStatusAsync(SelectedTable.Id, "Available");

                        // Update in our local collection
                        var tableInCollection = AvailableTables.FirstOrDefault(t => t.Id == SelectedTable.Id);
                        if (tableInCollection != null)
                        {
                            tableInCollection.Status = "Available";
                            OnPropertyChanged(nameof(AvailableTables));
                        }
                    }

                    // Notify UI of changes
                    OnPropertyChanged(nameof(CurrentTransaction));
                    OnPropertyChanged(nameof(CurrentTransaction.Details));
                    OnPropertyChanged(nameof(DiscountAmount));
                    OnPropertyChanged(nameof(StatusMessage));
                    OnPropertyChanged(nameof(SelectedCustomer));
                    OnPropertyChanged(nameof(CustomerSearchText));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing transaction: {ex.Message}");
                WindowManager.ShowError($"Error clearing transaction: {ex.Message}");
            }
        }

        private void CancelTransaction()
        {
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                WindowManager.ShowWarning("No items in transaction to cancel");
                return;
            }

            try
            {
                if (MessageBox.Show("Are you sure you want to cancel this transaction?", "Confirm Cancel",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    StartNewTransaction();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cancelling transaction: {ex.Message}");
                WindowManager.ShowError($"Error cancelling transaction: {ex.Message}");
            }
        }

        private void AddDiscount()
        {
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                WindowManager.ShowWarning("No items in transaction to apply discount");
                return;
            }

            WindowManager.InvokeAsync(() =>
            {
                try
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow;
                    var dialog = new DiscountDialog(TotalAmount);

                    dialog.Owner = mainWindow;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    if (dialog.ShowDialog() == true)
                    {
                        // Get the discount amount from the dialog
                        decimal discountAmount = dialog.DiscountAmount;

                        // Validate discount (assuming it's already a fixed amount)
                        if (discountAmount < 0)
                        {
                            MessageBox.Show(
                                "Discount cannot be negative. It has been corrected to 0.",
                                "Invalid Discount",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            discountAmount = 0;
                        }
                        else if (discountAmount > TotalAmount)
                        {
                            MessageBox.Show(
                                $"Discount cannot exceed total amount ({TotalAmount:C2}). It has been adjusted.",
                                "Invalid Discount",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            discountAmount = TotalAmount;
                        }

                        // Apply the validated discount
                        DiscountAmount = discountAmount;
                        UpdateTotals();

                        // If in restaurant mode and we have a selected table, update its state
                        if (IsRestaurantMode && SelectedTable != null && _tableTransactions.TryGetValue(SelectedTable.Id, out var state))
                        {
                            state.DiscountAmount = discountAmount;
                            state.TotalAmount = TotalAmount;
                            state.SubTotal = SubTotal;
                        }

                        // Notify UI of changes
                        OnPropertyChanged(nameof(DiscountAmount));
                        OnPropertyChanged(nameof(TotalAmount));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error applying discount: {ex.Message}");
                    MessageBox.Show($"An unexpected error occurred when applying discount. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }
    }
}