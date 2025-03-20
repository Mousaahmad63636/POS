using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Enums;
using QuickTechSystems.WPF.Commands;

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

        public ICommand CloseDrawerCommand { get; private set; }

         public ICommand SaveAsQuoteCommand { get; private set; }
        public ICommand SelectCategoryCommand { get; private set; }
        public ICommand ClearCustomerCommand { get; private set; }
        public ICommand LookupTransactionCommand { get; private set; }
        public ICommand IncrementTransactionIdCommand { get; private set; }
        public ICommand DecrementTransactionIdCommand { get; private set; }
        private void InitializeCommands()
        {

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

            VoidTransactionCommand = new AsyncRelayCommand(
                async _ => await VoidTransaction(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            ClearTransactionCommand = new RelayCommand(
                _ => ClearTransaction(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            CancelTransactionCommand = new RelayCommand(
                _ => CancelTransaction(),
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

            LookupTransactionCommand = new AsyncRelayCommand(
                async _ => await LookupTransactionAsync(),
                _ => !string.IsNullOrWhiteSpace(LookupTransactionId));

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
                        await UpdateExistingTransactionAsync();
                    }
                    else
                    {
                        await ProcessCashPayment();
                    }
                },
                _ => CurrentTransaction?.Details != null && CurrentTransaction.Details.Any());

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
                var result = MessageBox.Show(
                    "Do you want to exit without saving the sale?",
                    "Confirm Clear",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Clear directly
                    StartNewTransaction();
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

            var mainWindow = System.Windows.Application.Current.MainWindow;
            var dialog = new DiscountDialog(TotalAmount);

            WindowManager.InvokeAsync(() =>
            {
                try
                {
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