// Path: QuickTechSystems.WPF.ViewModels/MainStockViewModel.TransferOperations.cs
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class MainStockViewModel
    {
        private void ShowTransferDialog()
        {
            if (SelectedItem == null)
            {
                ShowTemporaryErrorMessage("Please select an item to transfer.");
                return;
            }

            TransferQuantity = 1;
            TransferByBoxes = false;
            _ = LoadStoreProductsAsync();
            OnPropertyChanged(nameof(SelectedItemBoxCount));
            IsTransferPopupOpen = true;
        }

        // in QuickTechSystems.WPF.ViewModels/MainStockViewModel.TransferOperations.cs
        private async Task TransferToStoreAsync()
        {
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

                decimal actualQuantity = TransferQuantity;

                if (TransferByBoxes)
                {
                    if (SelectedItem.ItemsPerBox <= 0)
                    {
                        ShowTemporaryErrorMessage("Items per box must be greater than zero for box transfers.");
                        return;
                    }

                    actualQuantity = TransferQuantity * SelectedItem.ItemsPerBox;
                }

                if (actualQuantity > SelectedItem.CurrentStock)
                {
                    ShowTemporaryErrorMessage($"Transfer quantity ({actualQuantity}) exceeds available stock ({SelectedItem.CurrentStock}).");
                    return;
                }

                // Store item details before the transfer
                int mainStockId = SelectedItem.MainStockId;
                int productId = SelectedStoreProduct.ProductId;
                string itemName = SelectedItem.Name;
                decimal oldStock = SelectedItem.CurrentStock;

                IsSaving = true;
                StatusMessage = "Processing transfer...";

                string transferredBy = "System User";

                bool transferSuccessful = false;

                try
                {
                    transferSuccessful = await _mainStockService.TransferToStoreAsync(
                        mainStockId,
                        productId,
                        actualQuantity,
                        transferredBy,
                        $"Manual transfer from MainStock to Store",
                        TransferByBoxes // Pass the flag indicating if this is a box transfer
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Transfer service error: {ex.Message}");
                    throw new InvalidOperationException($"Transfer failed: {ex.Message}", ex);
                }

                if (!transferSuccessful)
                {
                    throw new InvalidOperationException("Transfer failed. Please try again.");
                }

                // Update item in the collection
                var itemInCollection = Items.FirstOrDefault(i => i.MainStockId == mainStockId);
                if (itemInCollection != null)
                {
                    itemInCollection.CurrentStock -= actualQuantity;
                }

                // Update SelectedItem if it's still valid
                if (SelectedItem != null && SelectedItem.MainStockId == mainStockId)
                {
                    SelectedItem.CurrentStock -= actualQuantity;
                    OnPropertyChanged(nameof(SelectedItemBoxCount));
                }

                // Close the transfer popup
                IsTransferPopupOpen = false;

                // Force a full UI refresh after small delay to ensure transaction is complete
                await Task.Delay(300);

                try
                {
                    // Get updated item data
                    var updatedItem = await _mainStockService.GetByIdAsync(mainStockId);

                    // Update collection item if it exists and the updated item is valid
                    if (updatedItem != null)
                    {
                        if (itemInCollection != null)
                        {
                            itemInCollection.CurrentStock = updatedItem.CurrentStock;
                        }

                        // Update SelectedItem if it's still valid
                        if (SelectedItem != null && SelectedItem.MainStockId == mainStockId)
                        {
                            SelectedItem.CurrentStock = updatedItem.CurrentStock;
                            OnPropertyChanged(nameof(SelectedItemBoxCount));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error refreshing item data: {ex.Message}");
                    // Continue despite this error - the transfer was successful
                }

                // Complete refresh of all data
                await SafeLoadDataAsync();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    string unitType = TransferByBoxes ? "boxes" : "units";
                    MessageBox.Show(
                        $"Successfully transferred {TransferQuantity} {unitType} of {itemName} from MainStock to Store inventory.",
                        "Transfer Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Transfer error: {ex.Message}");
                string errorDetail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                string properErrorMessage = errorDetail.Replace("Payment failed:", "Transfer failed:");
                throw new InvalidOperationException(properErrorMessage, ex);
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }
        private async Task<MainStockDTO> GetUpdatedMainStockItemAsync(int mainStockId)
        {
            try
            {
                return await _mainStockService.GetByIdAsync(mainStockId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching updated MainStock item: {ex.Message}");
                return null;
            }
        }
    }
}