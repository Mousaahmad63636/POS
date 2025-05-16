using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.WPF.Views;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class BulkMainStockViewModel
    {
        private async Task LoadDataAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Loading data...";

                try
                {
                    var categories = await _categoryService.GetActiveAsync();
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Categories = new ObservableCollection<CategoryDTO>(categories);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading categories: {ex.Message}");
                    ShowTemporaryErrorMessage("Error loading categories. Functionality may be limited.");
                }

                try
                {
                    var suppliers = await _supplierService.GetActiveAsync();
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading suppliers: {ex.Message}");
                    ShowTemporaryErrorMessage("Error loading suppliers. Functionality may be limited.");
                }

                try
                {
                    var invoices = await _supplierInvoiceService.GetByStatusAsync("Draft");
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>(invoices);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading invoices: {ex.Message}");
                    ShowTemporaryErrorMessage("Error loading invoices. Functionality may be limited.");
                }

                StatusMessage = "Data loaded successfully.";
            }
            catch (Exception ex)
            {
                var errorMessage = GetDetailedErrorMessage(ex);
                StatusMessage = $"Error loading data: {errorMessage}";
                Debug.WriteLine($"Error in BulkMainStockViewModel.LoadDataAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error loading data: {errorMessage}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task<bool> SaveAllAsync()
        {
            bool semaphoreAcquired = false;

            try
            {
                semaphoreAcquired = await _operationLock.WaitAsync(DEFAULT_LOCK_TIMEOUT_MS);

                if (!semaphoreAcquired)
                {
                    Debug.WriteLine("Failed to acquire operation lock - operation already in progress");
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Operation already in progress. Please wait for the current operation to complete.",
                            "Operation in Progress", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    return false;
                }

                var validationResult = ValidateItems();
                if (!validationResult.IsValid)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Please fix the following issues before saving:\n\n{string.Join("\n", validationResult.ValidationErrors)}",
                            "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });

                    return false;
                }

                if (SelectedBulkInvoice == null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Please select a supplier invoice before saving items.",
                            "Invoice Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });

                    return false;
                }

                await Task.Run(() =>
                {
                    if (GenerateBarcodesForNewItems)
                    {
                        GenerateMissingBarcodes();
                    }
                });

                IsSaving = true;
                StatusMessage = "Preparing items for processing...";

                await Task.Run(() =>
                {
                    foreach (var item in Items)
                    {
                        item.CurrentStock = item.IndividualItems;
                        EnsureConsistentPricing(item);

                        if (item.ItemsPerBox <= 0)
                        {
                            item.ItemsPerBox = 0;
                        }

                        if (string.IsNullOrWhiteSpace(item.BoxBarcode) && !string.IsNullOrWhiteSpace(item.Barcode))
                        {
                            item.BoxBarcode = $"BX{item.Barcode}";
                        }

                        if (item.CreatedAt == default)
                        {
                            item.CreatedAt = DateTime.Now;
                        }

                        item.UpdatedAt = DateTime.Now;
                        item.SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId;
                    }
                });

                // Reset the queue service before enqueueing new items
                _bulkOperationQueueService.Reset();

                _bulkOperationQueueService.EnqueueItems(Items.ToList());

                var tcs = new TaskCompletionSource<bool>();
                BulkProcessingStatusWindow statusWindow = null;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var statusViewModel = new BulkProcessingStatusViewModel(_bulkOperationQueueService, _eventAggregator);
                    statusWindow = new BulkProcessingStatusWindow
                    {
                        Owner = GetOwnerWindow(),
                        DataContext = statusViewModel
                    };

                    statusViewModel.CloseRequested += (sender, args) =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            tcs.TrySetResult(args.DialogResult);
                        }
                        statusWindow.Close();
                    };

                    statusWindow.Closed += (sender, args) =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            tcs.TrySetResult(false);
                        }

                        // Ensure view model is disposed when window closes
                        if (statusWindow.DataContext is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    };

                    statusWindow.Show();
                });

                var result = await tcs.Task;

                if (result)
                {
                    await ProcessSupplierInvoiceIntegrationAsync();
                }

                // Always reset the queue service after the operation completes
                _bulkOperationQueueService.Reset();

                DialogResultBackup = result;
                DialogResult = result;

                return result;
            }
            catch (Exception ex)
            {
                string errorMessage = GetDetailedErrorMessage(ex);
                StatusMessage = $"Error preparing items: {errorMessage}";
                Debug.WriteLine($"Error in SaveAllAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error preparing items: {errorMessage}",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });

                // Reset the queue service on error
                _bulkOperationQueueService.Reset();

                return false;
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;

                if (semaphoreAcquired)
                {
                    try
                    {
                        _operationLock.Release();
                        Debug.WriteLine("Released operation lock in SaveAllAsync");
                    }
                    catch (SemaphoreFullException ex)
                    {
                        Debug.WriteLine($"Error releasing semaphore: {ex.Message}");
                    }
                }
            }
        }

        private async Task ProcessSupplierInvoiceIntegrationAsync()
        {
            try
            {
                if (SelectedBulkInvoice == null)
                {
                    Debug.WriteLine("No invoice selected for integration");
                    return;
                }

                StatusMessage = "Integrating with supplier invoice...";
                IsSaving = true;

                var savedItems = new List<MainStockDTO>();
                var barcodes = Items.Where(i => !string.IsNullOrWhiteSpace(i.Barcode))
                                   .Select(i => i.Barcode)
                                   .ToList();

                const int queryBatchSize = 10;
                for (int i = 0; i < barcodes.Count; i += queryBatchSize)
                {
                    var batch = barcodes.Skip(i).Take(queryBatchSize).ToList();

                    foreach (var barcode in batch)
                    {
                        try
                        {
                            var savedItem = await _mainStockService.GetByBarcodeAsync(barcode);
                            if (savedItem != null)
                            {
                                savedItems.Add(savedItem);
                            }

                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                StatusMessage = $"Finding items {savedItems.Count} of {barcodes.Count}...";
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error retrieving item with barcode {barcode}: {ex.Message}");
                        }
                    }

                    await Task.Delay(200);
                }

                int processedCount = 0;
                var totalCount = savedItems.Count;
                const int processBatchSize = 1;

                for (int i = 0; i < savedItems.Count; i += processBatchSize)
                {
                    var itemBatch = savedItems.Skip(i).Take(processBatchSize).ToList();

                    foreach (var mainStockItem in itemBatch)
                    {
                        try
                        {
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                StatusMessage = $"Creating products and invoice details ({i + 1}/{totalCount})...";
                            });

                            ProductDTO existingProduct = await _productService.FindProductByBarcodeAsync(mainStockItem.Barcode);

                            ProductDTO storeProduct;
                            if (existingProduct != null)
                            {
                                var productToUpdate = new ProductDTO
                                {
                                    ProductId = existingProduct.ProductId,
                                    Name = mainStockItem.Name,
                                    Barcode = mainStockItem.Barcode,
                                    BoxBarcode = mainStockItem.BoxBarcode,
                                    CategoryId = mainStockItem.CategoryId,
                                    CategoryName = mainStockItem.CategoryName,
                                    SupplierId = mainStockItem.SupplierId,
                                    SupplierName = mainStockItem.SupplierName,
                                    Description = mainStockItem.Description,
                                    PurchasePrice = mainStockItem.PurchasePrice,
                                    WholesalePrice = mainStockItem.WholesalePrice,
                                    SalePrice = mainStockItem.SalePrice,
                                    MainStockId = mainStockItem.MainStockId,
                                    BoxPurchasePrice = mainStockItem.BoxPurchasePrice,
                                    BoxWholesalePrice = mainStockItem.BoxWholesalePrice,
                                    BoxSalePrice = mainStockItem.BoxSalePrice,
                                    ItemsPerBox = mainStockItem.ItemsPerBox,
                                    MinimumBoxStock = mainStockItem.MinimumBoxStock,
                                    MinimumStock = mainStockItem.MinimumStock,
                                    ImagePath = mainStockItem.ImagePath,
                                    Speed = mainStockItem.Speed,
                                    IsActive = mainStockItem.IsActive,
                                    CurrentStock = existingProduct.CurrentStock,
                                    UpdatedAt = DateTime.Now
                                };

                                await _productService.UpdateAsync(productToUpdate);
                                await Task.Delay(100);
                                storeProduct = await _productService.FindProductByBarcodeAsync(mainStockItem.Barcode);
                            }
                            else
                            {
                                var newProduct = new ProductDTO
                                {
                                    Name = mainStockItem.Name,
                                    Barcode = mainStockItem.Barcode,
                                    BoxBarcode = mainStockItem.BoxBarcode,
                                    CategoryId = mainStockItem.CategoryId,
                                    CategoryName = mainStockItem.CategoryName,
                                    SupplierId = mainStockItem.SupplierId,
                                    SupplierName = mainStockItem.SupplierName,
                                    Description = mainStockItem.Description,
                                    PurchasePrice = mainStockItem.PurchasePrice,
                                    WholesalePrice = mainStockItem.WholesalePrice,
                                    SalePrice = mainStockItem.SalePrice,
                                    MainStockId = mainStockItem.MainStockId,
                                    BoxPurchasePrice = mainStockItem.BoxPurchasePrice,
                                    BoxWholesalePrice = mainStockItem.BoxWholesalePrice,
                                    BoxSalePrice = mainStockItem.BoxSalePrice,
                                    ItemsPerBox = mainStockItem.ItemsPerBox,
                                    MinimumBoxStock = mainStockItem.MinimumBoxStock,
                                    CurrentStock = 0,
                                    MinimumStock = mainStockItem.MinimumStock,
                                    ImagePath = mainStockItem.ImagePath,
                                    Speed = mainStockItem.Speed,
                                    IsActive = mainStockItem.IsActive,
                                    CreatedAt = DateTime.Now
                                };

                                await _productService.CreateAsync(newProduct);
                                await Task.Delay(100);
                                storeProduct = await _productService.FindProductByBarcodeAsync(mainStockItem.Barcode);
                            }

                            if (storeProduct?.ProductId > 0)
                            {
                                var invoiceDetail = new SupplierInvoiceDetailDTO
                                {
                                    SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId,
                                    ProductId = storeProduct.ProductId,
                                    ProductName = mainStockItem.Name,
                                    ProductBarcode = mainStockItem.Barcode,
                                    BoxBarcode = mainStockItem.BoxBarcode,
                                    NumberOfBoxes = mainStockItem.NumberOfBoxes,
                                    ItemsPerBox = mainStockItem.ItemsPerBox,
                                    BoxPurchasePrice = mainStockItem.BoxPurchasePrice,
                                    BoxSalePrice = mainStockItem.BoxSalePrice,
                                    Quantity = mainStockItem.CurrentStock,
                                    PurchasePrice = mainStockItem.PurchasePrice,
                                    TotalPrice = mainStockItem.PurchasePrice * mainStockItem.CurrentStock
                                };

                                await _supplierInvoiceService.AddProductToInvoiceAsync(invoiceDetail);
                                processedCount++;
                            }
                            else
                            {
                                Debug.WriteLine($"Warning: Could not create invoice detail for product {mainStockItem.Name} - Invalid ProductId");
                            }

                            await Task.Delay(200);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error processing integration for item {mainStockItem.Name}: {ex.Message}");
                        }
                    }
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = $"Successfully integrated {processedCount} items with invoice {SelectedBulkInvoice.InvoiceNumber}";
                });
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in supplier invoice integration: {ex.Message}");
                StatusMessage = $"Error integrating with invoice: {ex.Message}";
                await Task.Delay(1000);
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
            }
        }

        private void EnsureConsistentPricing(MainStockDTO item)
        {
            if (item.BoxPurchasePrice > 0 && item.PurchasePrice > 0)
            {
            }
            else if (item.PurchasePrice <= 0 && item.BoxPurchasePrice > 0 && item.ItemsPerBox > 0)
            {
                item.PurchasePrice = Math.Round(item.BoxPurchasePrice / item.ItemsPerBox, 2);
            }
            else if (item.BoxPurchasePrice <= 0 && item.PurchasePrice > 0 && item.ItemsPerBox > 0)
            {
                item.BoxPurchasePrice = Math.Round(item.PurchasePrice * item.ItemsPerBox, 2);
            }

            if (item.BoxWholesalePrice > 0 && item.WholesalePrice > 0)
            {
            }
            else if (item.WholesalePrice <= 0 && item.BoxWholesalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.WholesalePrice = Math.Round(item.BoxWholesalePrice / item.ItemsPerBox, 2);
            }
            else if (item.BoxWholesalePrice <= 0 && item.WholesalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.BoxWholesalePrice = Math.Round(item.WholesalePrice * item.ItemsPerBox, 2);
            }

            if (item.BoxSalePrice > 0 && item.SalePrice > 0)
            {
            }
            else if (item.SalePrice <= 0 && item.BoxSalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.SalePrice = Math.Round(item.BoxSalePrice / item.ItemsPerBox, 2);
            }
            else if (item.BoxSalePrice <= 0 && item.SalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.BoxSalePrice = Math.Round(item.SalePrice * item.ItemsPerBox, 2);
            }

            if (item.WholesalePrice <= 0 && item.PurchasePrice > 0)
            {
                item.WholesalePrice = Math.Round(item.PurchasePrice * 1.1m, 2);
            }

            if (item.SalePrice <= 0 && item.PurchasePrice > 0)
            {
                item.SalePrice = Math.Round(item.PurchasePrice * 1.2m, 2);
            }

            if (item.ItemsPerBox > 0)
            {
                if (item.BoxWholesalePrice <= 0 && item.WholesalePrice > 0)
                {
                    item.BoxWholesalePrice = Math.Round(item.WholesalePrice * item.ItemsPerBox, 2);
                }

                if (item.BoxSalePrice <= 0 && item.SalePrice > 0)
                {
                    item.BoxSalePrice = Math.Round(item.SalePrice * item.ItemsPerBox, 2);
                }
            }
        }

        private (bool IsValid, List<string> ValidationErrors) ValidateItems()
        {
            var validationErrors = new List<string>();

            if (Items.Count == 0)
            {
                validationErrors.Add("No items to save.");
                return (false, validationErrors);
            }

            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                var itemErrors = new List<string>();

                if (string.IsNullOrWhiteSpace(item.Name))
                    itemErrors.Add("Name is required");

                if (item.CategoryId <= 0)
                    itemErrors.Add("Category is required");

                if (!item.SupplierId.HasValue || item.SupplierId <= 0)
                    itemErrors.Add("Supplier is required");

                if (item.PurchasePrice <= 0 && item.BoxPurchasePrice <= 0)
                    itemErrors.Add("Either item purchase price or box purchase price is required");

                if (item.SalePrice <= 0 && item.BoxSalePrice <= 0)
                    itemErrors.Add("Either item sale price or box sale price is required");

                if (item.IndividualItems <= 0)
                    itemErrors.Add("Individual items quantity must be greater than zero");

                if (itemErrors.Count > 0)
                {
                    validationErrors.Add($"Item {i + 1} ({item.Name ?? "Unnamed"}): {string.Join(", ", itemErrors)}");
                }
            }

            var barcodes = new Dictionary<string, int>();
            var boxBarcodes = new Dictionary<string, int>();

            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];

                if (!string.IsNullOrWhiteSpace(item.Barcode))
                {
                    if (barcodes.TryGetValue(item.Barcode, out var index))
                    {
                        validationErrors.Add($"Duplicate barcode '{item.Barcode}' found in items {index + 1} and {i + 1}");
                    }
                    else
                    {
                        barcodes[item.Barcode] = i;
                    }
                }

                if (!string.IsNullOrWhiteSpace(item.BoxBarcode))
                {
                    if (boxBarcodes.TryGetValue(item.BoxBarcode, out var index))
                    {
                        validationErrors.Add($"Duplicate box barcode '{item.BoxBarcode}' found in items {index + 1} and {i + 1}");
                    }
                    else
                    {
                        boxBarcodes[item.BoxBarcode] = i;
                    }
                }

                if (!string.IsNullOrWhiteSpace(item.Barcode) &&
                    !string.IsNullOrWhiteSpace(item.BoxBarcode) &&
                    item.Barcode == item.BoxBarcode)
                {
                    validationErrors.Add($"Item {i + 1} ({item.Name}): Box barcode must be different from item barcode");
                }
            }

            return (validationErrors.Count == 0, validationErrors);
        }
        /// <summary>
        /// Generates barcodes for all items that don't have them.
        /// </summary>
        /// <summary>
        /// Generates barcodes for all items that don't have them.
        /// </summary>
        public void GenerateAllBarcodes()
        {
            try
            {
                int generatedCount = 0;
                var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
                var random = new Random();

                foreach (var item in Items)
                {
                    // Skip items that already have barcodes
                    if (!string.IsNullOrWhiteSpace(item.Barcode))
                        continue;

                    try
                    {
                        // Generate a unique barcode
                        var randomDigits = random.Next(1000, 9999).ToString();
                        var categoryPrefix = item.CategoryId > 0 ? item.CategoryId.ToString().PadLeft(3, '0') : "000";

                        // Add checksum for integrity
                        var baseCode = $"{categoryPrefix}{timestamp}{randomDigits}";
                        int sum = 0;
                        foreach (char c in baseCode)
                        {
                            if (char.IsDigit(c))
                            {
                                sum += (c - '0');
                            }
                        }
                        int checkDigit = sum % 10;

                        item.Barcode = $"{baseCode}{checkDigit}";
                        generatedCount++;

                        // Generate box barcode if empty
                        if (string.IsNullOrWhiteSpace(item.BoxBarcode))
                        {
                            item.BoxBarcode = $"BX{item.Barcode}";
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error generating barcode for item: {ex.Message}");
                        // Continue to next item
                    }
                }

                StatusMessage = $"Generated {generatedCount} barcodes for items without barcodes.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error generating barcodes: {ex.Message}";
                Debug.WriteLine($"Error in GenerateAllBarcodes: {ex}");
            }
        }
        private void GenerateMissingBarcodes()
        {
            var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
            var random = new Random();

            foreach (var item in Items.Where(i => string.IsNullOrWhiteSpace(i.Barcode)))
            {
                try
                {
                    var randomDigits = random.Next(1000, 9999).ToString();
                    var categoryPrefix = item.CategoryId > 0 ? item.CategoryId.ToString().PadLeft(3, '0') : "000";

                    var baseCode = $"{categoryPrefix}{timestamp}{randomDigits}";
                    int sum = 0;
                    foreach (char c in baseCode)
                    {
                        if (char.IsDigit(c))
                        {
                            sum += (c - '0');
                        }
                    }
                    int checkDigit = sum % 10;

                    item.Barcode = $"{baseCode}{checkDigit}";

                    if (string.IsNullOrWhiteSpace(item.BoxBarcode))
                    {
                        item.BoxBarcode = $"BX{item.Barcode}";
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error generating barcode for item: {ex.Message}");
                }
            }
        }

        // Keep only one implementation of GenerateAllBarcodes


        private string GetDetailedErrorMessage(Exception ex)
        {
            if (ex == null)
                return "Unknown error";

            var message = ex.Message;
            var currentEx = ex;

            while (currentEx.InnerException != null)
            {
                currentEx = currentEx.InnerException;
                message = currentEx.Message;
            }

            return message;
        }
    }
}