using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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

        public async Task<bool> SaveAllAsync()
        {
            try
            {
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

                // Generate barcodes on background thread
                await Task.Run(() =>
                {
                    if (GenerateBarcodesForNewItems)
                    {
                        GenerateMissingBarcodes();
                    }
                });

                IsSaving = true;
                StatusMessage = "Preparing items for processing...";

                // Prepare items on background thread
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

                // Queue items for processing
                _bulkOperationQueueService.EnqueueItems(Items.ToList());

                // Use TaskCompletionSource to wait for window to close
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

                    // Handle requested close through the ViewModel
                    statusViewModel.CloseRequested += (sender, args) =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            tcs.TrySetResult(args.DialogResult);
                        }
                        statusWindow.Close();
                    };

                    // Handle window closed event
                    statusWindow.Closed += (sender, args) =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            tcs.TrySetResult(false);
                        }
                    };

                    // Show window (non-modal)
                    statusWindow.Show();
                });

                // Wait for the window to close and get the result
                var result = await tcs.Task;

                if (result)
                {
                    await ProcessSupplierInvoiceIntegrationAsync();
                }

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

                return false;
            }
            finally
            {
                IsSaving = false;
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

                // Get barcodes first to avoid holding DB context open
                var barcodes = Items.Where(i => !string.IsNullOrWhiteSpace(i.Barcode))
                                   .Select(i => i.Barcode)
                                   .ToList();

                var savedItems = new List<MainStockDTO>();
                const int batchSize = 5; // Smaller batch size to avoid DB overload

                // Process in smaller batches
                for (int i = 0; i < barcodes.Count; i += batchSize)
                {
                    var batch = barcodes.Skip(i).Take(batchSize).ToList();

                    foreach (var barcode in batch)
                    {
                        try
                        {
                            // Get item from database
                            var savedItem = await _mainStockService.GetByBarcodeAsync(barcode);
                            if (savedItem != null)
                            {
                                savedItems.Add(savedItem);
                            }

                            // Update UI with progress
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                StatusMessage = $"Finding items {savedItems.Count} of {barcodes.Count}...";
                            });

                            // Give the UI thread time to update
                            await Task.Delay(50);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error retrieving item with barcode {barcode}: {ex.Message}");
                        }
                    }

                    // Delay between batches
                    await Task.Delay(100);
                }

                // Process store products and invoice details
                int processedCount = 0;
                var totalCount = savedItems.Count;

                for (int i = 0; i < savedItems.Count; i++)
                {
                    var mainStockItem = savedItems[i];
                    try
                    {
                        // Update UI with progress
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            StatusMessage = $"Creating products and invoice details ({i + 1}/{totalCount})...";
                        });

                        // Get or create store product
                        ProductDTO storeProduct = null;
                        var existingProduct = await _productService.FindProductByBarcodeAsync(mainStockItem.Barcode);

                        if (existingProduct != null)
                        {
                            // Update existing product
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

                            // Update product without storing the result
                            await _productService.UpdateAsync(productToUpdate);
                            storeProduct = productToUpdate; // Use the updated product
                        }
                        else
                        {
                            // Create new product
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

                            // Create product without storing the result
                            await _productService.CreateAsync(newProduct);

                            // After creation, get the product with its assigned ID
                            storeProduct = await _productService.FindProductByBarcodeAsync(mainStockItem.Barcode);

                            if (storeProduct == null)
                            {
                                // Fallback - use the created product
                                storeProduct = newProduct;
                                Debug.WriteLine($"Warning: Could not retrieve created product for {mainStockItem.Barcode}");
                            }
                        }

                        // Give DB a chance to complete the operation
                        await Task.Delay(50);

                        // Create invoice detail
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

                        // Give UI and DB a chance to breathe
                        if (i % 5 == 0)
                        {
                            await Task.Delay(100);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing integration for item {mainStockItem.Name}: {ex.Message}");
                    }
                }

                // Show success message
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
            // Handle purchase prices
            if (item.BoxPurchasePrice > 0 && item.PurchasePrice > 0)
            {
                // Both prices provided - respect user input
            }
            else if (item.PurchasePrice <= 0 && item.BoxPurchasePrice > 0 && item.ItemsPerBox > 0)
            {
                // Calculate item price from box price
                item.PurchasePrice = Math.Round(item.BoxPurchasePrice / item.ItemsPerBox, 2);
            }
            else if (item.BoxPurchasePrice <= 0 && item.PurchasePrice > 0 && item.ItemsPerBox > 0)
            {
                // Calculate box price from item price
                item.BoxPurchasePrice = Math.Round(item.PurchasePrice * item.ItemsPerBox, 2);
            }

            // Handle wholesale prices
            if (item.BoxWholesalePrice > 0 && item.WholesalePrice > 0)
            {
                // Both prices provided - respect user input
            }
            else if (item.WholesalePrice <= 0 && item.BoxWholesalePrice > 0 && item.ItemsPerBox > 0)
            {
                // Calculate item price from box price
                item.WholesalePrice = Math.Round(item.BoxWholesalePrice / item.ItemsPerBox, 2);
            }
            else if (item.BoxWholesalePrice <= 0 && item.WholesalePrice > 0 && item.ItemsPerBox > 0)
            {
                // Calculate box price from item price
                item.BoxWholesalePrice = Math.Round(item.WholesalePrice * item.ItemsPerBox, 2);
            }

            // Handle sale prices
            if (item.BoxSalePrice > 0 && item.SalePrice > 0)
            {
                // Both prices provided - respect user input
            }
            else if (item.SalePrice <= 0 && item.BoxSalePrice > 0 && item.ItemsPerBox > 0)
            {
                // Calculate item price from box price
                item.SalePrice = Math.Round(item.BoxSalePrice / item.ItemsPerBox, 2);
            }
            else if (item.BoxSalePrice <= 0 && item.SalePrice > 0 && item.ItemsPerBox > 0)
            {
                // Calculate box price from item price
                item.BoxSalePrice = Math.Round(item.SalePrice * item.ItemsPerBox, 2);
            }

            // Set defaults if needed
            if (item.WholesalePrice <= 0 && item.PurchasePrice > 0)
            {
                // Default wholesale price to purchase price + 10%
                item.WholesalePrice = Math.Round(item.PurchasePrice * 1.1m, 2);
            }

            if (item.SalePrice <= 0 && item.PurchasePrice > 0)
            {
                // Default sale price to purchase price + 20%
                item.SalePrice = Math.Round(item.PurchasePrice * 1.2m, 2);
            }

            // Calculate box prices if items per box is set
            if (item.ItemsPerBox > 0)
            {
                if (item.BoxWholesalePrice <= 0 && item.WholesalePrice > 0)
                {
                    // Default box wholesale price
                    item.BoxWholesalePrice = Math.Round(item.WholesalePrice * item.ItemsPerBox, 2);
                }

                if (item.BoxSalePrice <= 0 && item.SalePrice > 0)
                {
                    // Default box sale price
                    item.BoxSalePrice = Math.Round(item.SalePrice * item.ItemsPerBox, 2);
                }
            }
        }

        private (bool IsValid, List<string> ValidationErrors) ValidateItems()
        {
            var validationErrors = new List<string>();

            // Check for empty collection
            if (Items.Count == 0)
            {
                validationErrors.Add("No items to save.");
                return (false, validationErrors);
            }

            // Validate each item
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                var itemErrors = new List<string>();

                // Required field validation
                if (string.IsNullOrWhiteSpace(item.Name))
                    itemErrors.Add("Name is required");

                if (item.CategoryId <= 0)
                    itemErrors.Add("Category is required");

                if (!item.SupplierId.HasValue || item.SupplierId <= 0)
                    itemErrors.Add("Supplier is required");

                // Price validation
                if (item.PurchasePrice <= 0 && item.BoxPurchasePrice <= 0)
                    itemErrors.Add("Either item purchase price or box purchase price is required");

                if (item.SalePrice <= 0 && item.BoxSalePrice <= 0)
                    itemErrors.Add("Either item sale price or box sale price is required");

                // Quantity validation
                if (item.IndividualItems <= 0)
                    itemErrors.Add("Individual items quantity must be greater than zero");

                // Add errors for this item if any
                if (itemErrors.Count > 0)
                {
                    validationErrors.Add($"Item {i + 1} ({item.Name ?? "Unnamed"}): {string.Join(", ", itemErrors)}");
                }
            }

            // Check for duplicate barcodes
            var barcodes = new Dictionary<string, int>();
            var boxBarcodes = new Dictionary<string, int>();

            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];

                // Check item barcode for duplicates
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

                // Check box barcode for duplicates
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

                // Check if box barcode equals item barcode
                if (!string.IsNullOrWhiteSpace(item.Barcode) &&
                    !string.IsNullOrWhiteSpace(item.BoxBarcode) &&
                    item.Barcode == item.BoxBarcode)
                {
                    validationErrors.Add($"Item {i + 1} ({item.Name}): Box barcode must be different from item barcode");
                }
            }

            return (validationErrors.Count == 0, validationErrors);
        }

        private void GenerateMissingBarcodes()
        {
            var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
            var random = new Random();

            foreach (var item in Items.Where(i => string.IsNullOrWhiteSpace(i.Barcode)))
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

                // Generate box barcode if needed
                if (string.IsNullOrWhiteSpace(item.BoxBarcode))
                {
                    item.BoxBarcode = $"BX{item.Barcode}";
                }

                // Removed barcode image generation code
            }
        }

        private string GetDetailedErrorMessage(Exception ex)
        {
            if (ex == null)
                return "Unknown error";

            var message = ex.Message;
            var currentEx = ex;

            // Get the innermost exception message
            while (currentEx.InnerException != null)
            {
                currentEx = currentEx.InnerException;
                message = currentEx.Message;
            }

            return message;
        }
    }
}