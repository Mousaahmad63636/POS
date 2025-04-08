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
            // Preliminary check before entering the safe operation
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                await ShowErrorMessageAsync("You must select a product before completing the sale.");
                return;
            }

            await ShowLoadingAsync("Processing payment...", async () =>
            {
                // Validate active drawer
                var drawer = await _drawerService.GetCurrentDrawerAsync();
                if (drawer == null)
                {
                    throw new InvalidOperationException("No active cash drawer. Please open a drawer first.");
                }

                IDbContextTransaction dbTransaction = null;
                TransactionDTO result = null;
                try
                {
                    dbTransaction = await _unitOfWork.BeginTransactionAsync();

                    var currentUser = App.Current.Properties["CurrentUser"] as EmployeeDTO;

                    // IMPORTANT FIX: Apply discount proportionally to all line items if there's a transaction-level discount
                    if (DiscountAmount > 0 && CurrentTransaction.Details.Any())
                    {
                        // Calculate the discount ratio - how much of the original price we're discounting
                        decimal subtotal = CurrentTransaction.Details.Sum(d => d.Total);
                        decimal discountRatio = DiscountAmount / subtotal;

                        // Apply proportional discount to each line item
                        foreach (var detail in CurrentTransaction.Details)
                        {
                            // Calculate item-level discount
                            decimal itemDiscount = Math.Round(detail.Total * discountRatio, 2);

                            // Store the discount with the line item
                            detail.Discount = itemDiscount;

                            // Update the total for this item
                            detail.Total = Math.Round(detail.Total - itemDiscount, 2);

                            Debug.WriteLine($"Applied discount to {detail.ProductName}: Original=${detail.UnitPrice * detail.Quantity:F2}, " +
                                           $"Discount=${itemDiscount:F2}, Final=${detail.Total:F2}");
                        }
                    }

                    // Create transaction with properly discounted line items
                    var transactionToProcess = new TransactionDTO
                    {
                        TransactionDate = DateTime.Now,
                        CustomerId = SelectedCustomer?.CustomerId,
                        CustomerName = SelectedCustomer?.Name ?? "Walk-in Customer",
                        TotalAmount = TotalAmount, // This is already correctly calculated with discount
                        PaidAmount = TotalAmount,
                        TransactionType = TransactionType.Sale,
                        Status = TransactionStatus.Completed,
                        PaymentMethod = "Cash",
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
                                Discount = d.Discount, // Now includes the calculated item discount
                                Total = d.Total // Already updated to reflect the discount
                            }))
                    };

                    result = await _transactionService.ProcessSaleAsync(transactionToProcess);

                    await _drawerService.ProcessCashSaleAsync(
                        TotalAmount,
                        $"Transaction #{result.TransactionId}"
                    );

                    _eventAggregator.Publish(new DrawerUpdateEvent(
                        "Cash Sale",
                        TotalAmount,
                        $"Transaction #{result.TransactionId}"
                    ));

                    // Publish transaction event
                    _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>(
                        "Create",
                        result
                    ));

                    await dbTransaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Transaction error: {ex.Message}");

                    // Only roll back if transaction hasn't been committed or already rolled back
                    if (dbTransaction != null)
                    {
                        try
                        {
                            await dbTransaction.RollbackAsync();
                            Debug.WriteLine("Transaction successfully rolled back");
                        }
                        catch (Exception rollbackEx)
                        {
                            Debug.WriteLine($"Error during rollback: {rollbackEx.Message}");
                            throw new InvalidOperationException("Critical error: Transaction failed and rollback also failed. Please contact support.", rollbackEx);
                        }
                    }
                    throw;
                }

                // Only try to print receipt after transaction is fully committed
                if (result != null)
                {
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

                    StartNewTransaction();

                    // Update the lookup transaction ID with the next transaction number
                    try
                    {
                        await UpdateLookupTransactionIdAsync();
                    }
                    catch (Exception lookupEx)
                    {
                        Debug.WriteLine($"Error updating lookup ID: {lookupEx.Message}");
                        // Don't fail for lookup ID update
                    }

                    // Ensure drawer views are refreshed after a transaction
                    try
                    {
                        _eventAggregator.Publish(new DrawerUpdateEvent(
                            "Transaction Refresh",
                            0,
                            "Forced refresh after transaction"
                        ));
                    }
                    catch (Exception refreshEx)
                    {
                        Debug.WriteLine($"Error publishing refresh event: {refreshEx.Message}");
                    }
                }
            }, "Payment processed successfully");
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