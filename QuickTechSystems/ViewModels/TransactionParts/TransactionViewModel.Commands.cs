using System;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Domain.Enums;
using QuickTechSystems.WPF.Commands;
using Microsoft.Extensions.DependencyInjection; // For GetRequiredService
using QuickTechSystems.WPF.Services; // For IGlobalOverlayService

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
        public ICommand AddToCustomerDebtCommand { get; private set; }
        public ICommand ProcessReturnCommand { get; private set; }
        public ICommand SaveCustomerCommand { get; private set; }
        public ICommand ClearCustomerCommand { get; private set; }
        public ICommand CancelCustomerCommand { get; private set; }

        private void InitializeCommands()
        {
            // Existing commands
            ProcessBarcodeCommand = new AsyncRelayCommand(async _ => await ProcessBarcodeInput());
            SearchProductsCommand = new RelayCommand(_ => SearchProducts());
            CloseDrawerCommand = new AsyncRelayCommand(async _ => await CloseDrawerAsync());
            // Transaction management commands
            HoldTransactionCommand = new AsyncRelayCommand(async _ => await HoldTransaction());
            RecallTransactionCommand = new AsyncRelayCommand(async _ => await RecallTransaction());
            VoidTransactionCommand = new AsyncRelayCommand(async _ => await VoidTransaction());
            ClearTransactionCommand = new RelayCommand(_ => ClearTransaction());
            CancelTransactionCommand = new RelayCommand(_ => CancelTransaction());
            ChangeQuantityCommand = new AsyncRelayCommand(async _ => await ChangeQuantity());
            NewCustomerCommand = new AsyncRelayCommand(async _ => await ShowNewCustomerDialog());
            SaveCustomerCommand = new AsyncRelayCommand(async _ => await SaveNewCustomerAsync());
            CancelCustomerCommand = new RelayCommand(_ => CancelNewCustomer());
            NewCustomerCommand = new AsyncRelayCommand(async _ => await ShowNewCustomerDialog());
            SaveCustomerCommand = new AsyncRelayCommand(async _ => await SaveNewCustomerAsync());
            CancelCustomerCommand = new RelayCommand(_ => CancelNewCustomer());
            ClearCustomerCommand = new RelayCommand(_ => ClearCustomer());
            // Item management commands
            RemoveItemCommand = new RelayCommand(RemoveItem);
            VoidLastItemCommand = new RelayCommand(_ => VoidLastItem());
            ChangeQuantityCommand = new AsyncRelayCommand(async _ => await ChangeItemQuantityAsync());
            AddDiscountCommand = new RelayCommand(_ => AddDiscount());
            PriceCheckCommand = new AsyncRelayCommand(async _ => await CheckPrice());
            NewCustomerCommand = new AsyncRelayCommand(async _ => await ShowNewCustomerDialog());
            SaveCustomerCommand = new AsyncRelayCommand(async _ => await SaveNewCustomerAsync());
            CancelCustomerCommand = new RelayCommand(_ => CancelNewCustomer());
            // Customer commands
            NewCustomerCommand = new AsyncRelayCommand(async _ => await ShowNewCustomerDialog());

            // Payment commands
            CashPaymentCommand = new AsyncRelayCommand(async _ => await ProcessCashPayment());

            AddToCustomerDebtCommand = new AsyncRelayCommand(async _ => await AddToCustomerDebtAsync(),
       _ => SelectedCustomer != null && TotalAmount > 0);

            // Return and reprint commands
            ProcessReturnCommand = new AsyncRelayCommand(async _ => await ProcessReturn());
            ReprintLastCommand = new AsyncRelayCommand(async _ => await ReprintLast());
            PrintReceiptCommand = new AsyncRelayCommand(async _ => await PrintReceipt());

            SearchProductsCommand = new RelayCommand(_ =>
            {
                IsProductSearchVisible = true;
                SearchProducts();
            });

        }

        private async Task AddToCustomerDebtAsync()
        {
            try
            {
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    await ShowErrorMessageAsync("No items to add to customer debt");
                    return;
                }

                if (SelectedCustomer == null)
                {
                    await ShowErrorMessageAsync("Please select a customer");
                    return;
                }

                // Validate customer's credit limit
                if (SelectedCustomer.CreditLimit > 0 &&
                    (SelectedCustomer.Balance + TotalAmount) > SelectedCustomer.CreditLimit)
                {
                    await ShowErrorMessageAsync(
                        $"Adding this debt would exceed the customer's credit limit of {SelectedCustomer.CreditLimit:C2}");
                    return;
                }

                // Confirm debt transaction with user
                var confirmResult = MessageBox.Show(
                    $"Add {TotalAmount:C2} to {SelectedCustomer.Name}'s debt?",
                    "Confirm Debt Transaction",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmResult != MessageBoxResult.Yes)
                    return;

                // Begin database transaction
                using (var transaction = await _unitOfWork.BeginTransactionAsync())
                {
                    try
                    {
                        // Update customer balance
                        await _customerService.AddToBalanceAsync(SelectedCustomer.CustomerId, TotalAmount);

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

                        // Process sale transaction
                        var processedTransaction = await _transactionService.ProcessSaleAsync(CurrentTransaction);

                        // Create customer payment record
                        var customerPayment = new CustomerPaymentDTO
                        {
                            CustomerId = SelectedCustomer.CustomerId,
                            Amount = TotalAmount,
                            PaymentDate = DateTime.Now,
                            PaymentMethod = "Debt",
                            Notes = $"Debt transaction #{processedTransaction.TransactionId}"
                        };

                        // Save customer payment record
                        await _customerService.ProcessPaymentAsync(customerPayment);

                        // Commit transaction
                        await transaction.CommitAsync();

                        // Print receipt (optional)
                        var printReceipt = MessageBox.Show(
                            "Would you like to print a receipt?",
                            "Print Receipt",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (printReceipt == MessageBoxResult.Yes)
                        {
                            await PrintReceipt();
                        }

                        // Reset transaction and show success message
                        StartNewTransaction();
                        StatusMessage = $"Debt transaction completed for {SelectedCustomer.Name}";
                        MessageBox.Show(
                            $"Transaction of {TotalAmount:C2} has been added to {SelectedCustomer.Name}'s debt",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        // Publish events for system-wide updates
                        _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>(
                            "Update",
                            new CustomerDTO
                            {
                                CustomerId = SelectedCustomer.CustomerId,
                                Name = SelectedCustomer.Name,
                                Balance = TotalAmount,
                                CreditLimit = SelectedCustomer.CreditLimit
                            }));

                        _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>(
                            "Create",
                            processedTransaction));
                    }
                    catch (Exception innerEx)
                    {
                        // Rollback transaction on failure
                        await transaction.RollbackAsync();
                        throw new InvalidOperationException(
                            $"Error processing debt transaction: {innerEx.Message}", innerEx);
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle and log any unexpected errors
                await ShowErrorMessageAsync($"Unexpected error: {ex.Message}");

                // Optional: Log the full exception details
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
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
            if (parameter is TransactionDetailDTO detail && CurrentTransaction?.Details != null)
            {
                CurrentTransaction.Details.Remove(detail);
                UpdateTotals();
            }
        }

        private void VoidLastItem()
        {
            if (CurrentTransaction?.Details == null) return;

            var lastItem = CurrentTransaction.Details.LastOrDefault();
            if (lastItem != null)
            {
                CurrentTransaction.Details.Remove(lastItem);
                UpdateTotals();
            }
        }

        private void ClearTransaction()
        {
            if (MessageBox.Show("Are you sure you want to clear this transaction?", "Confirm Clear",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                StartNewTransaction();
            }
        }

        private void CancelTransaction()
        {
            if (MessageBox.Show("Are you sure you want to cancel this transaction?", "Confirm Cancel",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                StartNewTransaction();
            }
        }

        private void AddDiscount()
        {
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                WindowManager.ShowWarning("No items in transaction");
                return;
            }

            var mainWindow = System.Windows.Application.Current.MainWindow;
            var dialog = new DiscountDialog(TotalAmount);

            WindowManager.InvokeAsync(() =>
            {
                dialog.Owner = mainWindow;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (dialog.ShowDialog() == true)
                {
                    DiscountAmount = dialog.DiscountAmount;
                    UpdateTotals();
                }
            });
        }
    }
}