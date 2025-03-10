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
            if (CurrentTransaction?.Details == null) return;

            // Calculate subtotal from items
            ItemCount = CurrentTransaction.Details.Sum(d => d.Quantity);
            SubTotal = CurrentTransaction.Details.Sum(d => d.Total);

            // Calculate tax (assuming a tax rate, you might want to get this from settings)
            decimal taxRate = 0; // 11% tax rate - you should get this from your business settings
            TaxAmount = SubTotal * taxRate;

            // Calculate final total (subtotal + tax - discount)
            TotalAmount = SubTotal + TaxAmount - DiscountAmount;

            // Update LBP amount
            decimal lbpAmount = CurrencyHelper.ConvertToLBP(TotalAmount);
            TotalAmountLBP = CurrencyHelper.FormatLBP(lbpAmount);

            // Update all relevant properties
            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(SubTotal));
            OnPropertyChanged(nameof(TaxAmount));
            OnPropertyChanged(nameof(TotalAmount));
            OnPropertyChanged(nameof(TotalAmountLBP));
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
                await ShowErrorMessageAsync("No items in transaction to process payment");
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
                        Balance = 0,
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

                    var result = await _transactionService.ProcessSaleAsync(transactionToProcess);

                    await _drawerService.ProcessCashSaleAsync(
                        TotalAmount,
                        $"Transaction #{result.TransactionId}"
                    );

                    _eventAggregator.Publish(new DrawerUpdateEvent(
                        "Cash Sale",
                        TotalAmount,
                        $"Transaction #{result.TransactionId}"
                    ));

                    await dbTransaction.CommitAsync();

                    // Delay printing until after transaction is committed
                    await Task.Delay(100);

                    // Only try to print receipt after transaction is fully committed
                    try
                    {
                        await PrintReceipt();
                    }
                    catch (Exception printEx)
                    {
                        Debug.WriteLine($"Error printing receipt: {printEx.Message}");
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
            }, "Payment processed successfully");
        }

        private async Task ProcessReturn()
        {
            await ShowLoadingAsync("Processing return...", async () =>
            {
                // Validate drawer first
                var drawer = await _drawerService.GetCurrentDrawerAsync();
                if (drawer == null)
                {
                    throw new InvalidOperationException("No active cash drawer. Please open a drawer first.");
                }

                var dialog = new InputDialog("Process Return", "Enter transaction number to return");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (dialog.ShowDialog() == true)
                    {
                        if (!int.TryParse(dialog.Input, out int id))
                        {
                            throw new InvalidOperationException("Please enter a valid transaction number");
                        }

                        var transaction = await _transactionService.GetTransactionForReturnAsync(id);
                        if (transaction == null)
                        {
                            throw new InvalidOperationException("Transaction not found or not eligible for return");
                        }

                        var returnDialog = new ReturnItemSelectionDialog(transaction);
                        if (returnDialog.ShowDialog() == true)
                        {
                            var returnItems = returnDialog.SelectedItems;
                            if (returnItems.Any())
                            {
                                // Process return and update drawer
                                var result = await _transactionService.ProcessReturnAsync(id, returnItems);
                                var totalRefund = returnItems.Sum(ri => ri.RefundAmount);

                                // Process refund in drawer
                                await _drawerService.ProcessTransactionAsync(
                                    totalRefund,
                                    "Return",
                                    $"Return for transaction #{id}"
                                );

                                await PrintReturnReceipt(result, returnItems);
                            }
                            else
                            {
                                throw new InvalidOperationException("No items selected for return");
                            }
                        }
                    }
                });
            }, "Return processed successfully");
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

        private async Task ProcessReturnAsync(TransactionDTO transaction, List<ReturnItemDTO> returnItems)
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                decimal totalRefundAmount = returnItems.Sum(ri => ri.RefundAmount);
                var result = await _transactionService.ProcessReturnAsync(transaction.TransactionId, returnItems);

                // Clear current transaction
                StartNewTransaction();
            }, "Processing return");
        }

        private async Task ProcessRefund()
        {
            await ShowLoadingAsync("Processing refund...", async () =>
            {
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    throw new InvalidOperationException("No items to refund");
                }

                var transactionToRefund = new TransactionDTO
                {
                    TransactionDate = DateTime.Now,
                    CustomerId = SelectedCustomer?.CustomerId,
                    CustomerName = SelectedCustomer?.Name ?? "Walk-in Customer",
                    TotalAmount = TotalAmount,
                    TransactionType = TransactionType.Return,
                    Status = TransactionStatus.Completed,
                    Details = CurrentTransaction.Details
                };

                var result = await _transactionService.ProcessRefundAsync(transactionToRefund);
                await PrintReceipt();
                StartNewTransaction();
            }, "Refund processed successfully");
        }

        private async Task<List<ReturnItemDTO>> ShowReturnItemSelectionDialog(TransactionDTO transaction)
        {
            var returnItems = new List<ReturnItemDTO>();

            await WindowManager.InvokeAsync(() =>
            {
                var dialog = new ReturnItemSelectionDialog(transaction)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true)
                {
                    returnItems = dialog.SelectedItems;
                }
            });

            return returnItems;
        }

        private async Task PrintReturnReceipt(TransactionDTO transaction, List<ReturnItemDTO> returnItems)
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() != true)
                    return;

                var document = new FlowDocument();
                var paragraph = new Paragraph();

                // Header
                paragraph.Inlines.Add(new Bold(new Run("Return Receipt\n")) { FontSize = 16 });
                paragraph.Inlines.Add(new Run($"Original Transaction #: {transaction.TransactionId}\n"));
                paragraph.Inlines.Add(new Run($"Return Date: {DateTime.Now:g}\n"));
                paragraph.Inlines.Add(new Run($"Customer: {transaction.CustomerName}\n\n"));

                // Returned Items
                paragraph.Inlines.Add(new Bold(new Run("Returned Items:\n")));
                foreach (var item in returnItems)
                {
                    paragraph.Inlines.Add(new Run(
                        $"{item.ProductName}\n" +
                        $"Quantity: {item.QuantityToReturn}\n" +
                        $"Refund Amount: {item.RefundAmount:C2}\n" +
                        $"Reason: {item.ReturnReason}\n\n"));
                }

                // Total
                paragraph.Inlines.Add(new Bold(new Run($"Total Refund Amount: {returnItems.Sum(i => i.RefundAmount):C2}\n")));

                document.Blocks.Add(paragraph);
                printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Return Receipt");
            }, "Printing return receipt", "PrintOperation");
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
                    header.Inlines.Add("سوبرماركت هادي بلحص\n");
                    header.Inlines.Add(new Run("81052944")
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
                    AddTotalRow(totalsTable, "PAID:", lastTransaction.PaidAmount.ToString("C2"));
                    AddTotalRow(totalsTable, "CHANGE:", (lastTransaction.PaidAmount - lastTransaction.TotalAmount).ToString("C2"));

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