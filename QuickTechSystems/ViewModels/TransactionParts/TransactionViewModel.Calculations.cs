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
                DiscountAmount = Math.Max(0, DiscountAmount); // Ensure non-negative
                TotalAmount = 0;
                return;
            }

            try
            {
                // Calculate subtotal from items, handling potential null values
                ItemCount = CurrentTransaction.Details.Sum(d => d?.Quantity ?? 0);
                SubTotal = CurrentTransaction.Details.Sum(d => d?.Total ?? 0);

                // Tax is not used in this system
                TaxAmount = 0;

                // Validate discount amount
                DiscountAmount = Math.Max(0, DiscountAmount); // Ensure non-negative
                DiscountAmount = Math.Min(DiscountAmount, SubTotal); // Cap discount at subtotal

                // Calculate final total (subtotal - discount, no tax)
                TotalAmount = Math.Max(0, SubTotal - DiscountAmount);

                // Update LBP amount with proper error handling
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
        }

        private async Task ChangeQuantity()
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No items in transaction");
                }

                // Get the selected item or the last item added
                var selectedDetail = CurrentTransaction.Details.LastOrDefault();
                if (selectedDetail == null)
                {
                    throw new InvalidOperationException("No item selected");
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
                                "Quantity must be a positive number. It has been corrected to 1.",
                                "Invalid Quantity",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            dialog.NewQuantity = 1;
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
                    var transactionToProcess = new TransactionDTO
                    {
                        TransactionDate = DateTime.Now,
                        CustomerId = SelectedCustomer?.CustomerId,
                        CustomerName = SelectedCustomer?.Name ?? "Walk-in Customer",
                        TotalAmount = TotalAmount,
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
                                Discount = d.Discount,
                                Total = d.Total
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
                        }
                        catch (Exception rollbackEx)
                        {
                            Debug.WriteLine($"Error during rollback: {rollbackEx.Message}");
                            // Transaction might already be completed or rolled back
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

                await WindowManager.InvokeAsync(async () =>
                {
                    var printDialog = new PrintDialog();
                    if (printDialog.ShowDialog() != true) return;

                    var flowDocument = new FlowDocument
                    {
                        PageWidth = printDialog.PrintableAreaWidth,
                        ColumnWidth = printDialog.PrintableAreaWidth,
                        FontFamily = new FontFamily("Courier New"),
                        PagePadding = new Thickness(20, 0, 20, 0), // Matched with PrintReceipt
                        TextAlignment = TextAlignment.Center,
                        PageHeight = printDialog.PrintableAreaHeight
                    };

                    // Header Section
                    var header = new Paragraph
                    {
                        FontSize = 18, // Matched with PrintReceipt
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Navy
                    };
                    header.Inlines.Add("لقمة عبدو \n #l2met abdo \n ");

                    header.Inlines.Add(new Run("76437472")
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

                    foreach (var item in lastTransaction.Details)
                    {
                        var row = new TableRow();
                        row.Cells.Add(CreateCell(item.ProductName.Trim(), alignment: TextAlignment.Left));
                        row.Cells.Add(CreateCell(item.Quantity.ToString(), alignment: TextAlignment.Center));
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

                    printDialog.PrintDocument(
                        ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                        "Duplicate Receipt");
                });
            }, "Reprinting last receipt", "PrintOperation");
        }
    }
}