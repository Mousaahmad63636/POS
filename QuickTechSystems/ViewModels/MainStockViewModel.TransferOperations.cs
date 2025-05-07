// Path: QuickTechSystems.WPF.ViewModels/MainStockViewModel.TransferOperations.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.Services;
namespace QuickTechSystems.WPF.ViewModels
{
    public partial class MainStockViewModel
    {
        /// <summary>
        /// Show the transfer dialog for the selected item
        /// </summary>
        private void ShowTransferDialog()
        {
            if (SelectedItem == null)
            {
                ShowTemporaryErrorMessage("Please select an item to transfer.");
                return;
            }

            // Reset transfer quantity
            TransferQuantity = 1;

            // Default to item transfer instead of box transfer
            TransferByBoxes = false;

            // Refresh store products list
            _ = LoadStoreProductsAsync();

            // Explicitly refresh SelectedItemBoxCount before showing dialog
            OnPropertyChanged(nameof(SelectedItemBoxCount));

            // Show transfer dialog
            IsTransferPopupOpen = true;
        }

        /// <summary>
        /// Transfer the selected item to store inventory
        /// </summary>
        private async Task TransferToStoreAsync()
        {
            // Use reasonable timeout instead of 0
            if (!await _operationLock.WaitAsync(DEFAULT_LOCK_TIMEOUT_MS))
            {
                ShowTemporaryErrorMessage("Transfer operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (SelectedItem == null)
                {
                    ShowTemporaryErrorMessage("Please select an item to transfer.");
                    return;
                }

                if (SelectedStoreProduct == null)
                {
                    ShowTemporaryErrorMessage("Please select a store product to transfer to.");
                    return;
                }

                if (TransferQuantity <= 0)
                {
                    ShowTemporaryErrorMessage("Transfer quantity must be greater than zero.");
                    return;
                }

                // Calculate actual quantity to transfer based on transfer type
                decimal actualQuantity = TransferQuantity;

                // If transferring boxes, multiply by items per box
                if (TransferByBoxes)
                {
                    if (SelectedItem.ItemsPerBox <= 0)
                    {
                        ShowTemporaryErrorMessage("Items per box must be greater than zero for box transfers.");
                        return;
                    }

                    actualQuantity = TransferQuantity * SelectedItem.ItemsPerBox;
                }

                // Now check if we have enough stock
                if (actualQuantity > SelectedItem.CurrentStock)
                {
                    ShowTemporaryErrorMessage($"Transfer quantity ({actualQuantity}) exceeds available stock ({SelectedItem.CurrentStock}).");
                    return;
                }

                IsSaving = true;
                StatusMessage = "Processing transfer...";

                // Get the current user for the transfer record
                string transferredBy = "System User"; // You might want to get the actual user name from your app

                try
                {
                    // Make sure SelectedItem and SelectedStoreProduct still have their IDs
                    Debug.WriteLine($"Transfer details: MainStock ID: {SelectedItem.MainStockId}, Product ID: {SelectedStoreProduct.ProductId}, Quantity: {actualQuantity}");

                    // Create a transaction with timeouts and retries
                    int retries = 0;
                    bool transferSuccessful = false;
                    Exception lastException = null;

                    while (retries < 3 && !transferSuccessful)
                    {
                        try
                        {
                            // Create a cancellation token with timeout
                            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));

                            transferSuccessful = await _mainStockService.TransferToStoreAsync(
                                SelectedItem.MainStockId,
                                SelectedStoreProduct.ProductId,
                                actualQuantity,  // Use the calculated actual quantity
                                transferredBy,
                                $"Manual transfer from MainStock to Store"
                            );

                            if (!transferSuccessful)
                            {
                                throw new InvalidOperationException("Transfer returned false but did not throw an exception.");
                            }
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            retries++;

                            if (retries < 3)
                            {
                                Debug.WriteLine($"Transfer attempt {retries} failed: {ex.Message}. Retrying...");
                                await Task.Delay(500 * retries); // Increasing delay between retries
                            }
                        }
                    }

                    if (transferSuccessful)
                    {
                        // No need to close the popup here, it will be closed by the command handler

                        await SafeDispatcherOperation(() =>
                        {
                            MessageBox.Show(
                                $"Successfully transferred {actualQuantity} units from MainStock to Store inventory.",
                                "Transfer Successful",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                        });
                    }
                    else if (lastException != null)
                    {
                        // Format the error message
                        string errorDetail = lastException.InnerException != null ? lastException.InnerException.Message : lastException.Message;
                        string properErrorMessage = errorDetail.Replace("Payment failed:", "Transfer failed:");
                        throw new InvalidOperationException(properErrorMessage, lastException);
                    }
                    else
                    {
                        throw new InvalidOperationException("Transfer failed after multiple attempts. Please try again.");
                    }
                }
                catch (Exception ex)
                {
                    // Extract the real error message - fix the incorrect "Payment failed" message
                    string errorDetail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    string properErrorMessage = errorDetail.Replace("Payment failed:", "Transfer failed:");
                    throw new InvalidOperationException(properErrorMessage, ex);
                }
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }
    }
}