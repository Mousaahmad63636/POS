// Path: QuickTechSystems.WPF.ViewModels/BulkMainStockViewModel.DataOperations.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class BulkMainStockViewModel
    {
        /// <summary>
        /// Loads reference data for the view.
        /// </summary>
        private async Task LoadDataAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Loading data...";

                // Load categories
                var categories = await _categoryService.GetActiveAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Categories = new ObservableCollection<CategoryDTO>(categories);
                });

                // Load suppliers
                var suppliers = await _supplierService.GetActiveAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                });

                // Load draft supplier invoices
                var invoices = await _supplierInvoiceService.GetByStatusAsync("Draft");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>(invoices);
                });

                StatusMessage = "Data loaded successfully.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading data: {ex.Message}";
                Debug.WriteLine($"Error in BulkMainStockViewModel.LoadDataAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// Saves all items to the database with improved concurrency handling.
        /// </summary>
        public async Task<bool> SaveAllAsync()
        {
            try
            {
                // Validate items before attempting to save
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

                // Find existing products by barcode for updates
                var existingProductsMap = await FindExistingProductsAsync();

                // Set MainStockId for existing products and update box-related fields
                UpdateItemsWithExistingData(existingProductsMap);

                // Generate missing barcodes if needed
                if (GenerateBarcodesForNewItems)
                {
                    GenerateMissingBarcodes();
                }

                IsSaving = true;
                StatusMessage = "Saving items...";

                // Set up progress reporting
                TotalRows = Items.Count;
                CurrentRow = 0;

                var progress = new Progress<string>(status =>
                {
                    StatusMessage = status;
                    CurrentRow++;
                });

                // First stage: Save all MainStock items with full transaction management in service
                List<MainStockDTO> savedItems;
                try
                {
                    // Process items in smaller batches to reduce concurrency issues
                    var batchSize = 25; // Process 25 items at a time to reduce contention
                    var allItems = Items.ToList();
                    savedItems = new List<MainStockDTO>();

                    for (int i = 0; i < allItems.Count; i += batchSize)
                    {
                        var batch = allItems.Skip(i).Take(batchSize).ToList();
                        var batchProgress = new Progress<string>(status =>
                        {
                            StatusMessage = $"Batch {i / batchSize + 1}: {status}";
                            CurrentRow++;
                        });

                        var batchResult = await _mainStockService.CreateBatchAsync(batch, batchProgress);
                        savedItems.AddRange(batchResult);

                        // Small delay between batches to reduce database contention
                        await Task.Delay(200);
                    }

                    // Verify all items have valid MainStockIds
                    var invalidMainStockIds = savedItems.Where(item => item.MainStockId <= 0).ToList();

                    if (invalidMainStockIds.Any())
                    {
                        // Log detailed information about the invalid items
                        foreach (var item in invalidMainStockIds)
                        {
                            Debug.WriteLine($"Warning: Item {item.Name} has invalid MainStockId: {item.MainStockId}");
                        }

                        // Try a direct update as a fallback - update Items collection with any valid IDs
                        foreach (var savedItem in savedItems.Where(i => i.MainStockId > 0))
                        {
                            var matchingItem = Items.FirstOrDefault(i =>
                                i.Barcode == savedItem.Barcode ||
                                (string.IsNullOrEmpty(i.Barcode) && i.Name == savedItem.Name));

                            if (matchingItem != null)
                            {
                                matchingItem.MainStockId = savedItem.MainStockId;
                            }
                        }

                        // If we have some valid items, proceed with those
                        if (savedItems.Any(i => i.MainStockId > 0))
                        {
                            savedItems = savedItems.Where(i => i.MainStockId > 0).ToList();

                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show($"Only {savedItems.Count} of {Items.Count} items could be saved. " +
                                    $"You may need to try again with the remaining items.",
                                    "Partial Save", MessageBoxButton.OK, MessageBoxImage.Warning);
                            });
                        }
                        else
                        {
                            throw new InvalidOperationException("None of the items could be saved to the database. Please check your database connection and try again.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Exception during batch create: {ex}");
                    throw new InvalidOperationException($"Database error while saving items: {ex.Message}", ex);
                }

                // Second stage: Associate with invoice if selected
                if (SelectedBulkInvoice != null && savedItems.Any())
                {
                    await AssociateItemsWithInvoice(savedItems);
                }

                StatusMessage = $"Successfully saved {savedItems.Count} items.";

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Successfully saved {savedItems.Count} items.",
                        "Bulk Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                });

                // Wait a moment to ensure database operations are truly complete
                await Task.Delay(500);

                // First publish the global refresh event
                Debug.WriteLine("BulkMainStockViewModel: Publishing GlobalDataRefreshEvent");
                _eventAggregator.Publish(new GlobalDataRefreshEvent());

                // Give more time for event to propagate and avoid semaphore conflicts
                await Task.Delay(1200);

                // Then set dialog result to close the window
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Store a backup in case dialog is already closed
                    DialogResultBackup = true;
                    DialogResult = true;
                });

                return true;
            }
            catch (Exception ex)
            {
                // Get detailed error message from inner exceptions
                var errorMessage = ex.Message;
                var currentEx = ex;
                while (currentEx.InnerException != null)
                {
                    currentEx = currentEx.InnerException;
                    errorMessage += $"\n-> {currentEx.Message}";
                }

                StatusMessage = $"Error saving items: {errorMessage}";
                Debug.WriteLine($"Error in SaveAllAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error saving items: {errorMessage}",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });

                return false;
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// Validates all items before saving.
        /// </summary>
        private (bool IsValid, List<string> ValidationErrors) ValidateItems()
        {
            var validationErrors = new List<string>();
            var invalidItems = Items.Where(i =>
                string.IsNullOrWhiteSpace(i.Name) ||
                i.CategoryId <= 0 ||
                i.SalePrice <= 0 ||
                i.ItemsPerBox <= 0).ToList();

            if (invalidItems.Any())
            {
                foreach (var item in invalidItems)
                {
                    var fieldsMessage = "Missing required fields: ";
                    var fields = new List<string>();

                    if (string.IsNullOrWhiteSpace(item.Name))
                        fields.Add("Name");

                    if (item.CategoryId <= 0)
                        fields.Add("Category");

                    if (item.SalePrice <= 0)
                        fields.Add("Sale Price");

                    if (item.ItemsPerBox <= 0)
                        fields.Add("Items per Box");

                    fieldsMessage += string.Join(", ", fields);
                    validationErrors.Add($"• {item.Name ?? "Unnamed item"}: {fieldsMessage}");
                }
                return (false, validationErrors);
            }

            return (true, validationErrors);
        }

        /// <summary>
        /// Finds existing products by barcode.
        /// </summary>
        private async Task<Dictionary<string, MainStockDTO>> FindExistingProductsAsync()
        {
            var existingProductsMap = new Dictionary<string, MainStockDTO>();
            foreach (var item in Items.Where(i => !string.IsNullOrWhiteSpace(i.Barcode)))
            {
                try
                {
                    var existingProduct = await _mainStockService.GetByBarcodeAsync(item.Barcode);
                    if (existingProduct != null)
                    {
                        existingProductsMap[item.Barcode] = existingProduct;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error looking up existing product: {ex.Message}");
                }
            }
            return existingProductsMap;
        }

        /// <summary>
        /// Updates items with data from existing products.
        /// </summary>
        private void UpdateItemsWithExistingData(Dictionary<string, MainStockDTO> existingProductsMap)
        {
            foreach (var item in Items)
            {
                // Handle case for existing products
                if (!string.IsNullOrWhiteSpace(item.Barcode) &&
                    existingProductsMap.TryGetValue(item.Barcode, out var existingProduct))
                {
                    item.MainStockId = existingProduct.MainStockId;

                    // For existing items, add to current stock instead of replacing
                    decimal additionalStock = item.NumberOfBoxes * item.ItemsPerBox;
                    item.CurrentStock = existingProduct.CurrentStock + additionalStock;

                    // Keep any existing box-related properties that aren't being updated
                    if (string.IsNullOrWhiteSpace(item.BoxBarcode))
                    {
                        item.BoxBarcode = existingProduct.BoxBarcode;
                    }
                }
                else
                {
                    // For new items, set stock based on boxes and items per box
                    item.CurrentStock = item.NumberOfBoxes * item.ItemsPerBox;
                }

                // Apply consistent box barcode logic
                ApplyBoxBarcodeLogic(item);

                // Calculate prices based on box prices if needed
                CalculateItemPricesFromBoxPrices(item);
            }
        }

        /// <summary>
        /// Calculates item prices based on box prices if needed.
        /// </summary>
        private void CalculateItemPricesFromBoxPrices(MainStockDTO item)
        {
            // If item purchase price is zero but we have box price and items per box,
            // calculate the item purchase price
            if (item.PurchasePrice == 0 && item.BoxPurchasePrice > 0 && item.ItemsPerBox > 0)
            {
                item.PurchasePrice = item.BoxPurchasePrice / item.ItemsPerBox;
            }

            // Calculate item wholesale price if needed
            if (item.WholesalePrice == 0 && item.BoxWholesalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.WholesalePrice = item.BoxWholesalePrice / item.ItemsPerBox;
            }

            // Similarly for sale price
            if (item.SalePrice == 0 && item.BoxSalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.SalePrice = item.BoxSalePrice / item.ItemsPerBox;
            }
        }

        /// <summary>
        /// Applies consistent box barcode logic to an item.
        /// </summary>
        private void ApplyBoxBarcodeLogic(MainStockDTO item)
        {
            // Case 1: Empty box barcode - apply BX prefix to item barcode
            if (string.IsNullOrWhiteSpace(item.BoxBarcode) && !string.IsNullOrWhiteSpace(item.Barcode))
            {
                item.BoxBarcode = $"BX{item.Barcode}";
            }
            // Case 2: Box barcode equals item barcode - apply BX prefix
            else if (!string.IsNullOrWhiteSpace(item.BoxBarcode) && !string.IsNullOrWhiteSpace(item.Barcode)
                     && item.BoxBarcode == item.Barcode)
            {
                item.BoxBarcode = $"BX{item.Barcode}";
            }
        }

        /// <summary>
        /// Generates missing barcodes for items that don't have them.
        /// </summary>
        private void GenerateMissingBarcodes()
        {
            foreach (var item in Items.Where(i => string.IsNullOrWhiteSpace(i.Barcode)))
            {
                var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
                var random = new Random();
                var randomDigits = random.Next(1000, 9999).ToString();
                var categoryPrefix = item.CategoryId.ToString().PadLeft(3, '0');

                item.Barcode = $"{categoryPrefix}{timestamp}{randomDigits}";

                // Always generate a box barcode if item barcode exists
                if (string.IsNullOrWhiteSpace(item.BoxBarcode))
                {
                    item.BoxBarcode = $"BX{item.Barcode}";
                }

                // Generate barcode image
                try
                {
                    item.BarcodeImage = _barcodeService.GenerateBarcode(item.Barcode);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                    // Continue despite error
                }
            }
        }

        /// <summary>
        /// Associates saved items with the selected invoice.
        /// </summary>
        private async Task AssociateItemsWithInvoice(List<MainStockDTO> savedItems)
        {
            StatusMessage = $"Associating items with invoice {SelectedBulkInvoice.InvoiceNumber}...";
            int successCount = 0;
            int errorCount = 0;
            string lastErrorMessage = string.Empty;

            // Create a small delay to ensure all entities are properly saved before association
            await Task.Delay(500);

            // Process each item with careful error handling in smaller batches
            var batchSize = 10; // Process 10 items at a time

            for (int batchStart = 0; batchStart < savedItems.Count; batchStart += batchSize)
            {
                var batch = savedItems.Skip(batchStart).Take(batchSize).ToList();

                foreach (var item in batch)
                {
                    try
                    {
                        // Verify MainStockId exists and is valid
                        if (item.MainStockId <= 0)
                        {
                            Debug.WriteLine($"Invalid MainStockId for item {item.Name}");
                            errorCount++;
                            continue;
                        }

                        StatusMessage = $"Processing item {successCount + errorCount + 1} of {savedItems.Count}: {item.Name}";

                        // Get or create product - with isolation
                        ProductDTO storeProduct;

                        // First check if product already exists without tracking
                        var existingStoreProduct = await _productService.GetByBarcodeAsync(item.Barcode);

                        if (existingStoreProduct != null)
                        {
                            // Use a fresh, detached copy of the product to avoid tracking issues
                            storeProduct = new ProductDTO
                            {
                                ProductId = existingStoreProduct.ProductId,
                                Name = item.Name,
                                Barcode = item.Barcode,
                                BoxBarcode = item.BoxBarcode,
                                CategoryId = item.CategoryId,
                                CategoryName = item.CategoryName,
                                SupplierId = item.SupplierId,
                                SupplierName = item.SupplierName,
                                Description = item.Description,
                                PurchasePrice = item.PurchasePrice,
                                WholesalePrice = item.WholesalePrice,
                                SalePrice = item.SalePrice,
                                MainStockId = item.MainStockId,
                                BoxPurchasePrice = item.BoxPurchasePrice,
                                BoxWholesalePrice = item.BoxWholesalePrice,
                                BoxSalePrice = item.BoxSalePrice,
                                ItemsPerBox = item.ItemsPerBox,
                                MinimumBoxStock = item.MinimumBoxStock,
                                MinimumStock = item.MinimumStock,
                                ImagePath = item.ImagePath,
                                Speed = item.Speed,
                                IsActive = item.IsActive,
                                CurrentStock = existingStoreProduct.CurrentStock,
                                UpdatedAt = DateTime.Now
                            };

                            // Important: We need to actually update the product in the database
                            try
                            {
                                await _productService.UpdateAsync(storeProduct);
                                Debug.WriteLine($"Updated existing product {storeProduct.ProductId} with wholesale prices: Item={storeProduct.WholesalePrice}, Box={storeProduct.BoxWholesalePrice}");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error updating existing product with wholesale prices: {ex.Message}");
                                // Continue despite error - we don't want to fail the whole operation
                            }
                        }
                        else
                        {
                            // Create a fresh product object
                            storeProduct = new ProductDTO
                            {
                                Name = item.Name,
                                Barcode = item.Barcode,
                                BoxBarcode = item.BoxBarcode,
                                CategoryId = item.CategoryId,
                                CategoryName = item.CategoryName,
                                SupplierId = item.SupplierId,
                                SupplierName = item.SupplierName,
                                Description = item.Description,
                                PurchasePrice = item.PurchasePrice,
                                WholesalePrice = item.WholesalePrice,
                                SalePrice = item.SalePrice,
                                MainStockId = item.MainStockId,
                                BoxPurchasePrice = item.BoxPurchasePrice,
                                BoxWholesalePrice = item.BoxWholesalePrice,
                                BoxSalePrice = item.BoxSalePrice,
                                ItemsPerBox = item.ItemsPerBox,
                                MinimumBoxStock = item.MinimumBoxStock,
                                CurrentStock = 0,
                                MinimumStock = item.MinimumStock,
                                ImagePath = item.ImagePath,
                                Speed = item.Speed,
                                IsActive = item.IsActive,
                                CreatedAt = DateTime.Now
                            };

                            try
                            {
                                storeProduct = await _productService.CreateAsync(storeProduct);
                            }
                            catch (Exception ex)
                            {
                                throw new Exception($"Failed to create product: {ex.Message}", ex);
                            }
                        }

                        // Wait a moment to let any db operations complete
                        await Task.Delay(200);

                        // Now create the invoice detail - but with key focus on avoiding tracking conflicts
                        try
                        {
                            // Prepare the invoice detail object
                            var invoiceDetail = new SupplierInvoiceDetailDTO
                            {
                                SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId,
                                ProductId = storeProduct.ProductId,
                                ProductName = item.Name,
                                ProductBarcode = item.Barcode,
                                BoxBarcode = item.BoxBarcode,
                                NumberOfBoxes = item.NumberOfBoxes,
                                ItemsPerBox = item.ItemsPerBox,
                                BoxPurchasePrice = item.BoxPurchasePrice,
                                BoxSalePrice = item.BoxSalePrice,
                                Quantity = item.CurrentStock,
                                PurchasePrice = item.PurchasePrice,
                                TotalPrice = item.PurchasePrice * item.CurrentStock
                            };

                            // Process invoice detail in a completely separate operation
                            StatusMessage = $"Adding product to invoice: {item.Name}";
                            await _supplierInvoiceService.AddProductToInvoiceAsync(invoiceDetail);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to add product to invoice: {ex.Message}");
                            throw new Exception($"Failed to add product to invoice: {ex.Message}", ex);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        lastErrorMessage = ex.Message;
                        Debug.WriteLine($"Error associating item with invoice: {ex}");
                    }
                }

                // Add a delay between batches to avoid overwhelming the database
                await Task.Delay(300);
            }

            // Show a message with the results
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (successCount > 0)
                {
                    string message = $"Successfully added {successCount} products to invoice '{SelectedBulkInvoice.InvoiceNumber}'.";
                    if (errorCount > 0)
                    {
                        message += $"\n\n{errorCount} items couldn't be added. Last error: {lastErrorMessage}";
                    }

                    MessageBox.Show(message, "Invoice Association",
                        MessageBoxButton.OK, errorCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
                else if (errorCount > 0)
                {
                    MessageBox.Show($"Failed to add any products to the invoice. Error: {lastErrorMessage}",
                        "Invoice Association Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        /// <summary>
        /// Looks up a product by barcode.
        /// </summary>
        /// <param name="currentItem">The item to populate with product data.</param>
        private async Task LookupProductAsync(MainStockDTO currentItem)
        {
            if (currentItem == null || string.IsNullOrWhiteSpace(currentItem.Barcode))
                return;

            try
            {
                StatusMessage = "Searching for product...";
                IsSaving = true;

                // Look for existing product by barcode using a fresh DbContext
                var existingProduct = await _mainStockService.GetByBarcodeAsync(currentItem.Barcode);

                // If found, populate the current item with existing data
                if (existingProduct != null)
                {
                    // Keep the current box quantities
                    int numberOfBoxes = currentItem.NumberOfBoxes > 0 ? currentItem.NumberOfBoxes : 1;

                    // Copy all properties from existing product
                    currentItem.MainStockId = existingProduct.MainStockId;
                    currentItem.Name = existingProduct.Name;
                    currentItem.Description = existingProduct.Description;
                    currentItem.CategoryId = existingProduct.CategoryId;
                    currentItem.CategoryName = existingProduct.CategoryName;
                    currentItem.SupplierId = existingProduct.SupplierId;
                    currentItem.SupplierName = existingProduct.SupplierName;
                    currentItem.PurchasePrice = existingProduct.PurchasePrice;
                    currentItem.SalePrice = existingProduct.SalePrice;
                    currentItem.WholesalePrice = existingProduct.WholesalePrice;
                    currentItem.BoxBarcode = existingProduct.BoxBarcode;
                    currentItem.BoxPurchasePrice = existingProduct.BoxPurchasePrice;
                    currentItem.BoxSalePrice = existingProduct.BoxSalePrice;
                    currentItem.BoxWholesalePrice = existingProduct.BoxWholesalePrice;
                    currentItem.ItemsPerBox = existingProduct.ItemsPerBox > 0 ? existingProduct.ItemsPerBox : 1;
                    currentItem.MinimumStock = existingProduct.MinimumStock;
                    currentItem.MinimumBoxStock = existingProduct.MinimumBoxStock;
                    currentItem.Speed = existingProduct.Speed;
                    currentItem.IsActive = existingProduct.IsActive;
                    currentItem.ImagePath = existingProduct.ImagePath;

                    // Restore the number of boxes as this is likely what the user is adding
                    currentItem.NumberOfBoxes = numberOfBoxes;

                    // Generate barcode image if needed
                    if (currentItem.BarcodeImage == null)
                    {
                        try
                        {
                            currentItem.BarcodeImage = _barcodeService.GenerateBarcode(currentItem.Barcode);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                        }
                    }

                    StatusMessage = $"Found existing product: {currentItem.Name}";
                }
                else
                {
                    // Check if box barcode field is empty or the same as item barcode
                    if (!string.IsNullOrWhiteSpace(currentItem.BoxBarcode) &&
                        currentItem.BoxBarcode == currentItem.Barcode)
                    {
                        // Auto-prefix the box barcode with "BX" only when there's an exact match
                        currentItem.BoxBarcode = $"BX{currentItem.Barcode}";
                    }

                    StatusMessage = "New product. Please enter details.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error looking up product: {ex.Message}";
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// Looks up a product by box barcode.
        /// </summary>
        /// <param name="currentItem">The item to populate with product data.</param>
        private async Task LookupBoxBarcodeAsync(MainStockDTO currentItem)
        {
            if (currentItem == null || string.IsNullOrWhiteSpace(currentItem.BoxBarcode))
                return;

            try
            {
                StatusMessage = "Searching for product by box barcode...";
                IsSaving = true;

                // Look for existing product by box barcode
                var existingProduct = await _mainStockService.GetByBoxBarcodeAsync(currentItem.BoxBarcode);

                if (existingProduct != null)
                {
                    // Keep the current box quantities
                    int numberOfBoxes = currentItem.NumberOfBoxes > 0 ? currentItem.NumberOfBoxes : 1;

                    // Copy properties from existing product
                    currentItem.MainStockId = existingProduct.MainStockId;
                    currentItem.Name = existingProduct.Name;
                    currentItem.Barcode = existingProduct.Barcode;
                    currentItem.Description = existingProduct.Description;
                    currentItem.CategoryId = existingProduct.CategoryId;
                    currentItem.CategoryName = existingProduct.CategoryName;
                    currentItem.SupplierId = existingProduct.SupplierId;
                    currentItem.SupplierName = existingProduct.SupplierName;
                    currentItem.PurchasePrice = existingProduct.PurchasePrice;
                    currentItem.WholesalePrice = existingProduct.WholesalePrice;
                    currentItem.SalePrice = existingProduct.SalePrice;
                    currentItem.BoxPurchasePrice = existingProduct.BoxPurchasePrice;
                    currentItem.BoxWholesalePrice = existingProduct.BoxWholesalePrice;
                    currentItem.BoxSalePrice = existingProduct.BoxSalePrice;
                    currentItem.ItemsPerBox = existingProduct.ItemsPerBox > 0 ? existingProduct.ItemsPerBox : 1;
                    currentItem.MinimumStock = existingProduct.MinimumStock;
                    currentItem.MinimumBoxStock = existingProduct.MinimumBoxStock;
                    currentItem.Speed = existingProduct.Speed;
                    currentItem.IsActive = existingProduct.IsActive;
                    currentItem.ImagePath = existingProduct.ImagePath;

                    // Restore the number of boxes
                    currentItem.NumberOfBoxes = numberOfBoxes;

                    // Generate barcode image if needed
                    if (currentItem.BarcodeImage == null && !string.IsNullOrWhiteSpace(currentItem.Barcode))
                    {
                        try
                        {
                            currentItem.BarcodeImage = _barcodeService.GenerateBarcode(currentItem.Barcode);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                        }
                    }

                    StatusMessage = $"Found existing product by box barcode: {currentItem.Name}";
                }
                else
                {
                    StatusMessage = "No product found with this box barcode.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error looking up box barcode: {ex.Message}";
                Debug.WriteLine($"Error in LookupBoxBarcodeAsync: {ex}");
            }
            finally
            {
                IsSaving = false;
            }
        }
    }
}