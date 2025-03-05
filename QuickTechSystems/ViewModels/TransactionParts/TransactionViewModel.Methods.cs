using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Domain.Enums;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel
    {
        public Window OwnerWindow { get; set; }
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
            return System.Windows.Application.Current.MainWindow ??
                WindowManager.InvokeAsync(() => System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault()).Result;
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

            ClearTotals();
        }

        private async Task HoldTransaction()
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
        }

        private async Task RecallTransaction()
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
        }
        private async Task UpdateUI(Action action)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(action);
        }
        private async Task VoidTransaction()
        {
            if (CurrentTransaction == null) return;

            if (MessageBox.Show("Are you sure you want to void this transaction?", "Confirm Void",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await _transactionService.UpdateStatusAsync(CurrentTransaction.TransactionId, TransactionStatus.Cancelled);
                StartNewTransaction();
            }
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
                MessageBox.Show(GetOwnerWindow(),
                    message,
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information)
            );
        }
        public async Task ProcessBarcodeInput()
        {
            if (string.IsNullOrEmpty(BarcodeText)) return;

            try
            {
                var product = await _productService.GetByBarcodeAsync(BarcodeText);
                if (product != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        AddProductToTransaction(product);
                    });
                }
                else
                {
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show("Product not found", "Error", MessageBoxButton.OK, MessageBoxImage.Warning));
                }
            }
            catch (Exception ex)
            {
                await WindowManager.InvokeAsync(() =>
                    MessageBox.Show($"Error processing barcode: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
            finally
            {
                BarcodeText = string.Empty;
            }
        }

        private void AddProductToTransaction(ProductDTO product)
        {
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
                existingDetail.Quantity++;
                existingDetail.Total = existingDetail.Quantity * existingDetail.UnitPrice;
                // Force UI update for the existing item
                var index = CurrentTransaction.Details.IndexOf(existingDetail);
                CurrentTransaction.Details.RemoveAt(index);
                CurrentTransaction.Details.Insert(index, existingDetail);
            }
            else
            {
                var detail = new TransactionDetailDTO
                {
                    ProductId = product.ProductId,
                    ProductName = product.Name,
                    ProductBarcode = product.Barcode,
                    Quantity = 1,
                    UnitPrice = product.SalePrice,
                    PurchasePrice = product.PurchasePrice,
                    Total = product.SalePrice,
                    TransactionId = CurrentTransaction.TransactionId
                };

                CurrentTransaction.Details.Add(detail);
            }

            UpdateTotals();
            OnPropertyChanged(nameof(CurrentTransaction.Details));
        }
        private async Task<bool?> ShowDialog<T>(T dialog) where T : Window
        {
            return await WindowManager.InvokeAsync(() =>
            {
                dialog.Owner = OwnerWindow ?? System.Windows.Application.Current.MainWindow;
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

        private new async Task ShowErrorMessageAsync(string message)
        {
            await WindowManager.InvokeAsync(() => WindowManager.ShowError(message));
        }
    }
}