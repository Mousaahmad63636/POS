using System;
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.WPF.Views;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Domain.Enums;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Windows.Documents;
using System.Text;
using System.Windows.Media;
using System.Windows.Controls;
using QuickTechSystems.Application.Helpers;
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.Events;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel
    {
        private void ClearTotals()
        {
            ItemCount = 0;
            SubTotal = 0;
            TaxAmount = 0;
            DiscountAmount = 0;
            TotalAmount = 0;
        }

        public void UpdateTotals()
        {
            if (CurrentTransaction?.Details == null)
            {
                // Reset totals if no transaction details
                ItemCount = 0;
                SubTotal = 0;
                TaxAmount = 0;
                DiscountAmount = 0;
                TotalAmount = 0;
                TotalAmountLBP = "0 LBP";
                return;
            }

            try
            {
                // Calculate values using precise decimal arithmetic
                ItemCount = CurrentTransaction.Details.Sum(d => d?.Quantity ?? 0m);

                // Recalculate each detail's total to ensure accuracy with decimal quantities
                foreach (var detail in CurrentTransaction.Details)
                {
                    if (detail != null)
                    {
                        // Ensure each line item total is calculated precisely
                        detail.Total = decimal.Multiply(detail.Quantity, detail.UnitPrice) - detail.Discount;
                    }
                }

                // Calculate subtotal as sum of all item totals
                SubTotal = CurrentTransaction.Details.Sum(d => d?.Total ?? 0m);

                // Tax is not used in this system
                TaxAmount = 0;

                // Validate discount amount
                DiscountAmount = Math.Max(0, DiscountAmount); // Ensure non-negative
                DiscountAmount = Math.Min(DiscountAmount, SubTotal); // Cap discount at subtotal

                // Calculate final total (subtotal - discount, no tax)
                TotalAmount = decimal.Subtract(SubTotal, DiscountAmount);
                TotalAmount = Math.Max(0, TotalAmount); // Ensure non-negative

                // Update LBP amount
                try
                {
                    decimal lbpAmount = CurrencyHelper.ConvertToLBP(TotalAmount);
                    TotalAmountLBP = CurrencyHelper.FormatLBP(lbpAmount);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error converting to LBP: {ex.Message}");
                    TotalAmountLBP = "Error converting";
                }

                // Force update all transaction totals
                if (CurrentTransaction != null)
                {
                    CurrentTransaction.TotalAmount = TotalAmount;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating totals: {ex.Message}");
                // Provide default values in case of calculation error
                ItemCount = CurrentTransaction.Details.Count;
                SubTotal = 0;
                TaxAmount = 0;
                TotalAmount = 0;
                TotalAmountLBP = "0 LBP";
            }

            // Update all relevant properties
            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(SubTotal));
            OnPropertyChanged(nameof(TaxAmount));
            OnPropertyChanged(nameof(TotalAmount));
            OnPropertyChanged(nameof(TotalAmountLBP));
            OnPropertyChanged(nameof(DiscountAmount));
            OnPropertyChanged(nameof(CurrentTransaction));
        }
        private async Task ChangeQuantity()
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
                    var dialog = new QuantityDialog(selectedDetail.ProductName, selectedDetail.Quantity)
                    {
                        Owner = System.Windows.Application.Current.MainWindow
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        // Validate new quantity
                        if (dialog.NewQuantity <= 0)
                        {
                            MessageBox.Show(
                                "Quantity must be a positive number. It has been corrected to 0.01.",
                                "Invalid Quantity",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            dialog.NewQuantity = 0.01m;
                        }

                        // Update quantity and recalculate total
                        selectedDetail.Quantity = dialog.NewQuantity;
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
            }, "Changing quantity");
        }

        private async Task ProcessCashPayment()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No items in transaction to process");
                }

                // Validate active drawer
                var drawer = await _drawerService.GetCurrentDrawerAsync();
                if (drawer == null)
                {
                    throw new InvalidOperationException("No active cash drawer. Please open a drawer first.");
                }

                // Prepare transaction data
                var transactionToProcess = new TransactionDTO
                {
                    TransactionDate = DateTime.Now,
                    CustomerId = SelectedCustomer?.CustomerId,
                    CustomerName = SelectedCustomer?.Name ?? "Walk-in Customer",
                    TotalAmount = TotalAmount,
                    PaidAmount = TotalAmount, // Full payment for cash transactions
                    TransactionType = TransactionType.Sale,
                    Status = TransactionStatus.Completed,
                    PaymentMethod = "Cash",
                    CashierId = App.Current.Properties["CurrentUser"] is EmployeeDTO employee ? employee.EmployeeId.ToString() : "0",
                    CashierName = App.Current.Properties["CurrentUser"] is EmployeeDTO emp ? emp.FullName : "Unknown",
                    Details = new ObservableCollection<TransactionDetailDTO>(CurrentTransaction.Details.Select(d => new TransactionDetailDTO
                    {
                        ProductId = d.ProductId,
                        ProductName = d.ProductName,
                        ProductBarcode = d.ProductBarcode,
                        CategoryId = d.CategoryId,
                        Quantity = d.Quantity,
                        UnitPrice = d.UnitPrice,
                        PurchasePrice = d.PurchasePrice,
                        Discount = d.Discount,
                        Total = d.Total
                    }))
                };

                // Store current table ID before processing
                int? tableId = SelectedTable?.Id;

                // Use database transaction to ensure atomicity
                using var dbTransaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    // Process the transaction in the database
                    var result = await _transactionService.ProcessSaleAsync(transactionToProcess);

                    // Update the drawer cash amount
                    await _drawerService.ProcessCashSaleAsync(TotalAmount, $"Transaction #{result.TransactionId}");

                    // Update table status if in restaurant mode
                    if (IsRestaurantMode && tableId.HasValue)
                    {
                        // Remove this table's transaction from our dictionary
                        if (_tableTransactions.ContainsKey(tableId.Value))
                        {
                            _tableTransactions.Remove(tableId.Value);
                        }

                        await _restaurantTableService.UpdateTableStatusAsync(tableId.Value, "Available");

                        // Update the table status in the local collection as well
                        var tableInCollection = AvailableTables.FirstOrDefault(t => t.Id == tableId.Value);
                        if (tableInCollection != null)
                        {
                            tableInCollection.Status = "Available";
                            // Publish event to notify other components of table status change
                            _eventAggregator.Publish(new EntityChangedEvent<RestaurantTableDTO>("Update", tableInCollection));
                        }
                    }

                    await dbTransaction.CommitAsync();

                    // Process receipt printing
                    await PrintReceipt();

                    // Show success message
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show(
                            $"Transaction completed successfully. Amount: {TotalAmount:C2}",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information));

                    // Update financial summary
                    await UpdateFinancialSummaryAsync();

                    // If we're in restaurant mode, clear the selected table
                    if (IsRestaurantMode)
                    {
                        SelectedTable = null;
                    }

                    // Clear the current transaction and start a new one
                    StartNewTransaction(false); // Pass false to prevent table selection dialog

                    // Update the lookup transaction ID with the next transaction number
                    await UpdateLookupTransactionIdAsync();

                    // Update status
                    StatusMessage = "Transaction completed successfully";
                    OnPropertyChanged(nameof(StatusMessage));
                }
                catch (Exception ex)
                {
                    await dbTransaction.RollbackAsync();
                    Debug.WriteLine($"Error processing cash payment: {ex.Message}");
                    throw new InvalidOperationException($"Failed to process payment: {ex.Message}", ex);
                }
            }, "Processing cash payment");
        }
        private async Task<bool> ValidateDrawerAsync()
        {
            var drawer = await _drawerService.GetCurrentDrawerAsync();
            if (drawer == null)
            {
                await ShowErrorMessageAsync("No active cash drawer. Please open a drawer first.");
                return false;
            }
            return true;
        }

        private async Task ReprintLast()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                var lastTransaction = await _transactionService.GetLastTransactionAsync();
                if (lastTransaction == null || !lastTransaction.Details.Any())
                {
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show("No previous transaction found or transaction details are missing", "Information",
                            MessageBoxButton.OK, MessageBoxImage.Information));
                    return;
                }

                // Retrieve company information from business settings
                string companyName;
                string phoneNumber;

                try
                {
                    companyName = await _businessSettingsService.GetSettingValueAsync("CompanyName", "اوتوماتيكو كافي");
                    phoneNumber = await _businessSettingsService.GetSettingValueAsync("PhoneNumber", "71999795 / 03889591");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error retrieving business settings: {ex.Message}");
                    companyName = "اوتوماتيكو كافي"; // Default value
                    phoneNumber = "71999795 / 03889591"; // Default value
                }

                await WindowManager.InvokeAsync(async () =>
                {
                    var printDialog = new PrintDialog();
                    if (printDialog.ShowDialog() != true) return;

                    var flowDocument = new FlowDocument
                    {
                        PageWidth = printDialog.PrintableAreaWidth,
                        ColumnWidth = printDialog.PrintableAreaWidth,
                        FontFamily = new FontFamily("Courier New"),
                        PagePadding = new Thickness(20, 0, 20, 0),
                        TextAlignment = TextAlignment.Center,
                        PageHeight = printDialog.PrintableAreaHeight
                    };

                    // Header Section
                    var header = new Paragraph
                    {
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Navy
                    };
                    header.Inlines.Add($"{companyName}\n");

                    header.Inlines.Add(new Run(phoneNumber)
                    { FontSize = 14, FontWeight = FontWeights.Normal });
                    flowDocument.Blocks.Add(header);
                    flowDocument.Blocks.Add(CreateDivider());

                    // Transaction Metadata
                    var metaTable = new Table { FontSize = 11.5, CellSpacing = 0 };
                    metaTable.Columns.Add(new TableColumn { Width = new GridLength(120) });
                    metaTable.Columns.Add(new TableColumn { Width = GridLength.Auto });
                    metaTable.RowGroups.Add(new TableRowGroup());

                    AddMetaRow(metaTable, "TRX #:", lastTransaction.TransactionId.ToString());
                    AddMetaRow(metaTable, "DATE:", lastTransaction.TransactionDate.ToString("MM/dd/yyyy hh:mm tt"));
                    AddMetaRow(metaTable, "CUSTOMER:", lastTransaction.CustomerName ?? "Walk-in Customer");
                    flowDocument.Blocks.Add(metaTable);
                    flowDocument.Blocks.Add(CreateDivider());

                    // Items Table
                    var itemsTable = new Table { FontSize = 12, CellSpacing = 0 };
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(4, GridUnitType.Star) });
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });

                    itemsTable.RowGroups.Add(new TableRowGroup());
                    var headerRow = new TableRow { Background = Brushes.LightGray };
                    headerRow.Cells.Add(CreateCell("ITEM", FontWeights.Bold, TextAlignment.Left));
                    headerRow.Cells.Add(CreateCell("QTY", FontWeights.Bold, TextAlignment.Center));
                    headerRow.Cells.Add(CreateCell("PRICE", FontWeights.Bold, TextAlignment.Right));
                    headerRow.Cells.Add(CreateCell("TOTAL", FontWeights.Bold, TextAlignment.Right));
                    itemsTable.RowGroups[0].Rows.Add(headerRow);

                    // In the loop for creating receipt items
                    foreach (var item in lastTransaction.Details)
                    {
                        var row = new TableRow();
                        row.Cells.Add(CreateCell(item.ProductName.Trim(), alignment: TextAlignment.Left));
                        row.Cells.Add(CreateCell(item.Quantity.ToString("0.##"), alignment: TextAlignment.Center));
                        row.Cells.Add(CreateCell(item.UnitPrice.ToString("C2"), alignment: TextAlignment.Right));
                        row.Cells.Add(CreateCell(item.Total.ToString("C2"), alignment: TextAlignment.Right));
                        itemsTable.RowGroups[0].Rows.Add(row);
                    }
                    flowDocument.Blocks.Add(itemsTable);
                    flowDocument.Blocks.Add(CreateDivider());

                    // Totals Section
                    var totalsTable = new Table { FontSize = 12, CellSpacing = 0 };
                    totalsTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
                    totalsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });

                    totalsTable.RowGroups.Add(new TableRowGroup());
                    var subtotal = lastTransaction.Details.Sum(d => d.Total);
                    AddTotalRow(totalsTable, "SUBTOTAL:", subtotal.ToString("C2"));
                    AddTotalRow(totalsTable, "TOTAL:", lastTransaction.TotalAmount.ToString("C2"), true);

                    flowDocument.Blocks.Add(totalsTable);
                    flowDocument.Blocks.Add(CreateDivider());

                    // Footer Section (Reprint indicator)
                    var footer = new Paragraph
                    {
                        FontSize = 12,
                        Foreground = Brushes.Gray,
                        TextAlignment = TextAlignment.Center
                    };
                    footer.Inlines.Add("*** DUPLICATE RECEIPT ***");
                    flowDocument.Blocks.Add(footer);

                    try
                    {
                        printDialog.PrintDocument(
                            ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                            "Duplicate Receipt");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Print error: {ex.Message}");
                        MessageBox.Show(
                            "Error printing receipt. Please check your printer connection and try again.",
                            "Print Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                });
            }, "Reprinting last receipt", "PrintOperation");
        }
    }
}