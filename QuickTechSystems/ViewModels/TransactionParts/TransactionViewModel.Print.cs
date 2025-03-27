using System;
using System.Diagnostics;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel
    {
        private async Task PrintReceipt()
        {
            // Check for active transaction before starting
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                await ShowErrorMessageAsync("No transaction to print. Please add items first.");
                return;
            }

            await ExecuteOperationSafelyAsync(async () =>
            {
                bool printCancelled = false;
                StatusMessage = "Preparing receipt...";
                OnPropertyChanged(nameof(StatusMessage));

                // Get the latest transaction ID before printing
                int transactionId;
                try
                {
                    transactionId = await _transactionService.GetLatestTransactionIdAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting latest transaction ID: {ex.Message}");
                    transactionId = CurrentTransaction?.TransactionId ?? 0;
                }

                // All UI operations in a dedicated block
                await WindowManager.InvokeAsync(async () =>
                {
                    StatusMessage = "Opening print dialog...";
                    OnPropertyChanged(nameof(StatusMessage));

                    // Check printer availability first
                    try
                    {
                        bool printerAvailable = false;
                        await Task.Run(() => {
                            try
                            {
                                PrintServer printServer = new PrintServer();
                                PrintQueueCollection printQueues = printServer.GetPrintQueues();
                                printerAvailable = printQueues.Count() > 0;
                            }
                            catch (Exception)
                            {
                                printerAvailable = false;
                            }
                        });

                        if (!printerAvailable)
                        {
                            MessageBox.Show(
                                "No printer available. Please connect a printer and try again.",
                                "Printer Required",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            printCancelled = true;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error checking printer availability: {ex.Message}");
                        MessageBox.Show(
                            "Unable to check printer availability. Please ensure a printer is properly configured.",
                            "Printer Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        printCancelled = true;
                        return;
                    }

                    // Prepare print dialog safely
                    PrintDialog printDialog = new PrintDialog();
                    bool? dialogResult = false;

                    try
                    {
                        // Show print dialog on UI thread
                        dialogResult = printDialog.ShowDialog();
                    }
                    catch (Exception dialogEx)
                    {
                        Debug.WriteLine($"Error showing print dialog: {dialogEx.Message}");
                        MessageBox.Show(
                            "Failed to open print dialog. Please check printer configuration.",
                            "Print Dialog Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        printCancelled = true;
                        return;
                    }

                    if (dialogResult != true)
                    {
                        printCancelled = true;
                        return;
                    }

                    StatusMessage = "Preparing document...";
                    OnPropertyChanged(nameof(StatusMessage));

                    try
                    {
                        var flowDocument = CreateReceiptDocument(printDialog, transactionId);

                        // Execute printing on UI thread with proper error handling
                        try
                        {
                            StatusMessage = "Printing...";
                            OnPropertyChanged(nameof(StatusMessage));

                            printDialog.PrintDocument(
                                ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                                "Transaction Receipt");

                            // Update status message on success
                            StatusMessage = "Receipt printed successfully";
                            OnPropertyChanged(nameof(StatusMessage));
                        }
                        catch (Exception printEx)
                        {
                            Debug.WriteLine($"Error during print execution: {printEx.Message}");
                            MessageBox.Show(
                                "Error printing receipt. Please check printer connection and try again.",
                                "Print Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            StatusMessage = "Print error - Receipt not printed";
                            OnPropertyChanged(nameof(StatusMessage));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error preparing receipt: {ex.Message}");
                        MessageBox.Show(
                            "An error occurred while preparing the receipt. Please try again.",
                            "Print Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Error preparing receipt";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                });

                // Set appropriate status message if print was cancelled
                if (printCancelled)
                {
                    StatusMessage = "Printing was cancelled";
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }, "Printing receipt", "PrintOperation");
        }

        // New method to create the receipt document - extracted for clarity
        private FlowDocument CreateReceiptDocument(PrintDialog printDialog, int transactionId)
        {
            var flowDocument = new FlowDocument
            {
                PageWidth = printDialog.PrintableAreaWidth,
                ColumnWidth = printDialog.PrintableAreaWidth,
                FontFamily = new FontFamily("Courier New"),
                PagePadding = new Thickness(20, 0, 20, 0),
                TextAlignment = TextAlignment.Center,
                PageHeight = printDialog.PrintableAreaHeight
            };

            // 1. Header Section
            var header = new Paragraph
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Navy
            };
            header.Inlines.Add(" اوتوماتيكو كافي \n ");

            header.Inlines.Add(new Run("71999795 / 03889591")
            { FontSize = 14, FontWeight = FontWeights.Normal });
            flowDocument.Blocks.Add(header);
            flowDocument.Blocks.Add(CreateDivider());

            // 2. Transaction Metadata
            var metaTable = new Table { FontSize = 11.5, CellSpacing = 0 };
            metaTable.Columns.Add(new TableColumn { Width = new GridLength(120) });
            metaTable.Columns.Add(new TableColumn { Width = GridLength.Auto });
            metaTable.RowGroups.Add(new TableRowGroup());
            AddMetaRow(metaTable, "TRX #:", transactionId.ToString());
            AddMetaRow(metaTable, "DATE:", DateTime.Now.ToString("MM/dd/yyyy hh:mm tt"));
            if (SelectedCustomer != null)
                AddMetaRow(metaTable, "CUSTOMER:", SelectedCustomer.Name);
            flowDocument.Blocks.Add(metaTable);
            flowDocument.Blocks.Add(CreateDivider());

            // 3. Items Table
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

            if (CurrentTransaction?.Details != null)
            {
                foreach (var item in CurrentTransaction.Details)
                {
                    var row = new TableRow();
                    row.Cells.Add(CreateCell(item.ProductName?.Trim() ?? "Unknown", alignment: TextAlignment.Left));
                    row.Cells.Add(CreateCell(item.Quantity.ToString(), alignment: TextAlignment.Center));
                    row.Cells.Add(CreateCell(item.UnitPrice.ToString("C2"), alignment: TextAlignment.Right));
                    row.Cells.Add(CreateCell(item.Total.ToString("C2"), alignment: TextAlignment.Right));
                    itemsTable.RowGroups[0].Rows.Add(row);
                }
            }
            flowDocument.Blocks.Add(itemsTable);
            flowDocument.Blocks.Add(CreateDivider());

            // 4. Totals Section
            var totalsTable = new Table { FontSize = 12, CellSpacing = 0 };
            totalsTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            totalsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
            totalsTable.RowGroups.Add(new TableRowGroup());
            AddTotalRow(totalsTable, "SUBTOTAL:", SubTotal.ToString("C2"));
            if (DiscountAmount > 0)
                AddTotalRow(totalsTable, "DISCOUNT:", $"-{DiscountAmount:C2}");
            AddTotalRow(totalsTable, "TOTAL:", TotalAmount.ToString("C2"), true);

            // Add LBP amount if available
            if (!string.IsNullOrEmpty(TotalAmountLBP) && TotalAmountLBP != "Error converting" && TotalAmountLBP != "0 LBP")
            {
                AddTotalRow(totalsTable, "TOTAL LBP:", TotalAmountLBP, false);
            }

            flowDocument.Blocks.Add(totalsTable);
            flowDocument.Blocks.Add(CreateDivider());

            return flowDocument;
        }

        // Helper methods
        private TableCell CreateCell(string text, FontWeight fontWeight = default, TextAlignment alignment = TextAlignment.Left)
        {
            var paragraph = new Paragraph(new Run(text ?? string.Empty))
            {
                FontWeight = fontWeight,
                TextAlignment = alignment
            };
            return new TableCell(paragraph);
        }

        private void AddMetaRow(Table table, string label, string value)
        {
            if (table == null) return;

            var row = new TableRow();
            row.Cells.Add(CreateCell(label, FontWeights.Bold));
            row.Cells.Add(CreateCell(value ?? string.Empty));
            table.RowGroups[0].Rows.Add(row);
        }

        private void AddTotalRow(Table table, string label, string value, bool isBold = false)
        {
            if (table == null) return;

            var row = new TableRow();
            row.Cells.Add(CreateCell(label, isBold ? FontWeights.Bold : FontWeights.Normal, TextAlignment.Left));
            row.Cells.Add(CreateCell(value, isBold ? FontWeights.Bold : FontWeights.Normal, TextAlignment.Right));
            table.RowGroups[0].Rows.Add(row);
        }

        private BlockUIContainer CreateDivider()
        {
            return new BlockUIContainer(new Border
            {
                Height = 1,
                Background = Brushes.Black,
                Margin = new Thickness(0, 2, 0, 2) // Reduced top and bottom margin
            });
        }
    }
}