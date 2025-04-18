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
using QuickTechSystems.Application.Helpers;

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

                // Create a snapshot of the current transaction to prevent any modifications during printing
                var transactionSnapshot = new TransactionDTO
                {
                    TransactionId = CurrentTransaction.TransactionId,
                    TransactionDate = CurrentTransaction.TransactionDate,
                    CustomerId = CurrentTransaction.CustomerId,
                    CustomerName = CurrentTransaction.CustomerName,
                    TotalAmount = TotalAmount,
                    Details = new ObservableCollection<TransactionDetailDTO>(
                        CurrentTransaction.Details.Select(d => new TransactionDetailDTO
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

                // Get the latest transaction ID before printing
                int transactionId;
                try
                {
                    transactionId = await _transactionService.GetLatestTransactionIdAsync();
                    if (transactionId <= 0)
                    {
                        // If we couldn't get a valid ID, use the current transaction ID or a placeholder
                        transactionId = CurrentTransaction.TransactionId > 0 ?
                            CurrentTransaction.TransactionId :
                            (int)(DateTime.Now.Ticks % 10000);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting latest transaction ID: {ex.Message}");
                    transactionId = CurrentTransaction.TransactionId > 0 ?
                        CurrentTransaction.TransactionId :
                        (int)(DateTime.Now.Ticks % 10000);
                }

                // Capture the current subtotal and discount amount for receipt printing
                decimal currentSubTotal = SubTotal;
                decimal currentDiscountAmount = DiscountAmount;

                Debug.WriteLine($"Printing transaction with {transactionSnapshot.Details.Count} items, " +
                    $"Total: {transactionSnapshot.TotalAmount}, Discount: {currentDiscountAmount}");

                // Retrieve all business settings for the receipt
                string companyName;
                string address;
                string phoneNumber;
                string email;
                string footerText1;
                string footerText2;

                try
                {
                    // Get all business settings with defaults if not found
                    companyName = await _businessSettingsService.GetSettingValueAsync("CompanyName", "Your Business Name");
                    address = await _businessSettingsService.GetSettingValueAsync("Address", "Your Business Address");
                    phoneNumber = await _businessSettingsService.GetSettingValueAsync("Phone", "Your Phone Number");
                    email = await _businessSettingsService.GetSettingValueAsync("Email", "");
                    footerText1 = await _businessSettingsService.GetSettingValueAsync("ReceiptFooter1", "Stay caffeinated!!");
                    footerText2 = await _businessSettingsService.GetSettingValueAsync("ReceiptFooter2", "See you next time");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error retrieving business settings: {ex.Message}");
                    // Default values if settings retrieval fails
                    companyName = "Your Business Name";
                    address = "Your Business Address";
                    phoneNumber = "Your Phone Number";
                    email = "";
                    footerText1 = "Stay caffeinated!!";
                    footerText2 = "See you next time";
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
                        // Pass the business settings to the document creation method
                        var flowDocument = CreateReceiptDocument(
                            printDialog,
                            transactionId,
                            companyName,
                            address,
                            phoneNumber,
                            email,
                            footerText1,
                            footerText2,
                            transactionSnapshot,
                            currentSubTotal,
                            currentDiscountAmount);

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

        private FlowDocument CreateReceiptDocument(
            PrintDialog printDialog,
            int transactionId,
            string companyName,
            string address,
            string phoneNumber,
            string email,
            string footerText1,
            string footerText2,
            TransactionDTO transaction,
            decimal subTotal,
            decimal discountAmount)
        {
            var flowDocument = new FlowDocument
            {
                PageWidth = printDialog.PrintableAreaWidth,
                ColumnWidth = printDialog.PrintableAreaWidth,
                FontFamily = new FontFamily("Arial"),
                FontWeight = FontWeights.Normal,
                PagePadding = new Thickness(10, 0, 10, 0),
                TextAlignment = TextAlignment.Center,
                PageHeight = printDialog.PrintableAreaHeight
            };

            // 1. Header Section - Updated layout with smaller fonts
            var header = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };

            // Company Name
            header.Inlines.Add(new Run(companyName)
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black
            });
            header.Inlines.Add(new LineBreak());

            // Company Address
            if (!string.IsNullOrWhiteSpace(address))
            {
                header.Inlines.Add(new Run(address)
                {
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                });
                header.Inlines.Add(new LineBreak());
            }

            // Phone Number
            if (!string.IsNullOrWhiteSpace(phoneNumber))
            {
                header.Inlines.Add(new Run(phoneNumber)
                {
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                });
            }

            // Email address
            if (!string.IsNullOrWhiteSpace(email))
            {
                header.Inlines.Add(new LineBreak());
                header.Inlines.Add(new Run(email)
                {
                    FontSize = 10,
                    FontWeight = FontWeights.Normal
                });
            }

            flowDocument.Blocks.Add(header);
            flowDocument.Blocks.Add(CreateDivider());

            // 2. Transaction Metadata
            var metaTable = new Table { FontSize = 10, CellSpacing = 0 };
            metaTable.Columns.Add(new TableColumn { Width = new GridLength(80) });
            metaTable.Columns.Add(new TableColumn { Width = GridLength.Auto });
            metaTable.RowGroups.Add(new TableRowGroup());
            AddMetaRow(metaTable, "TRX #:", transactionId.ToString());
            AddMetaRow(metaTable, "DATE:", DateTime.Now.ToString("MM/dd/yyyy hh:mm tt"));
            if (transaction.CustomerName != null && !string.IsNullOrEmpty(transaction.CustomerName))
                AddMetaRow(metaTable, "CUSTOMER:", transaction.CustomerName);
            flowDocument.Blocks.Add(metaTable);
            flowDocument.Blocks.Add(CreateDivider());

            // 3. Items Table
            var itemsTable = new Table { FontSize = 10, CellSpacing = 0 };
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

            // Default exchange rate if the helper fails
            const decimal DEFAULT_EXCHANGE_RATE = 90000m;

            // Calculate totals directly from the transaction
            if (transaction?.Details != null && transaction.Details.Any())
            {
                foreach (var item in transaction.Details)
                {
                    var row = new TableRow();

                    // Convert unit price to LBP
                    decimal lbpUnitPrice;
                    try
                    {
                        lbpUnitPrice = CurrencyHelper.ConvertToLBP(item.UnitPrice);
                        if (lbpUnitPrice == 0 && item.UnitPrice > 0)
                        {
                            lbpUnitPrice = item.UnitPrice * DEFAULT_EXCHANGE_RATE;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error converting unit price to LBP: {ex.Message}");
                        lbpUnitPrice = item.UnitPrice * DEFAULT_EXCHANGE_RATE;
                    }

                    // Convert total to LBP
                    decimal lbpItemTotal;  // Renamed from lbpTotal to avoid conflict
                    try
                    {
                        lbpItemTotal = CurrencyHelper.ConvertToLBP(item.Total);
                        if (lbpItemTotal == 0 && item.Total > 0)
                        {
                            lbpItemTotal = item.Total * DEFAULT_EXCHANGE_RATE;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error converting line total to LBP: {ex.Message}");
                        lbpItemTotal = item.Total * DEFAULT_EXCHANGE_RATE;
                    }

                    row.Cells.Add(CreateCell(item.ProductName?.Trim() ?? "Unknown", FontWeights.Normal, TextAlignment.Left));
                    row.Cells.Add(CreateCell(item.Quantity.ToString(), FontWeights.Normal, TextAlignment.Center));
                    row.Cells.Add(CreateCell($"{lbpUnitPrice:N0} LBP", FontWeights.Normal, TextAlignment.Right));
                    row.Cells.Add(CreateCell($"{lbpItemTotal:N0} LBP", FontWeights.Normal, TextAlignment.Right));
                    itemsTable.RowGroups[0].Rows.Add(row);
                }
            }
            else
            {
                Debug.WriteLine("Warning: No transaction details available for receipt");

                // Add a row indicating no items
                var emptyRow = new TableRow();
                emptyRow.Cells.Add(CreateCell("No items available", FontWeights.Normal, TextAlignment.Center));
                emptyRow.Cells.Add(CreateCell("", FontWeights.Normal, TextAlignment.Center));
                emptyRow.Cells.Add(CreateCell("", FontWeights.Normal, TextAlignment.Center));
                emptyRow.Cells.Add(CreateCell("", FontWeights.Normal, TextAlignment.Center));
                itemsTable.RowGroups[0].Rows.Add(emptyRow);
            }

            flowDocument.Blocks.Add(itemsTable);
            flowDocument.Blocks.Add(CreateDivider());

            // 4. Totals Section - ONLY show LBP amount
            var totalsTable = new Table { FontSize = 11, CellSpacing = 0 };
            totalsTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            totalsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
            totalsTable.RowGroups.Add(new TableRowGroup());

            // Add subtotal row in LBP
            decimal lbpSubtotal;
            try
            {
                // Try using the helper first
                lbpSubtotal = CurrencyHelper.ConvertToLBP(subTotal);

                // If we get zero but have a non-zero subtotal, use default rate
                if (lbpSubtotal == 0 && subTotal > 0)
                {
                    lbpSubtotal = subTotal * DEFAULT_EXCHANGE_RATE;
                }

                Debug.WriteLine($"USD Subtotal: {subTotal}, LBP Subtotal: {lbpSubtotal}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting subtotal to LBP: {ex.Message}");
                lbpSubtotal = subTotal * DEFAULT_EXCHANGE_RATE;
            }

            AddTotalRow(totalsTable, "SUBTOTAL:", $"{lbpSubtotal:N0} LBP");

            // Add discount row if applicable
            decimal lbpDiscount = 0;
            if (discountAmount > 0)
            {
                try
                {
                    lbpDiscount = CurrencyHelper.ConvertToLBP(discountAmount);
                    if (lbpDiscount == 0)
                    {
                        lbpDiscount = discountAmount * DEFAULT_EXCHANGE_RATE;
                    }
                }
                catch
                {
                    lbpDiscount = discountAmount * DEFAULT_EXCHANGE_RATE;
                }

                AddTotalRow(totalsTable, "DISCOUNT:", $"-{lbpDiscount:N0} LBP");
            }

            // Calculate and add ONLY total in LBP
            decimal total = Math.Max(0, subTotal - discountAmount);
            decimal lbpFinalTotal;  // Renamed from lbpTotal to avoid conflict

            try
            {
                lbpFinalTotal = CurrencyHelper.ConvertToLBP(total);
                if (lbpFinalTotal == 0 && total > 0)
                {
                    lbpFinalTotal = total * DEFAULT_EXCHANGE_RATE;
                }
                Debug.WriteLine($"USD Total: {total}, LBP Total: {lbpFinalTotal}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting total to LBP: {ex.Message}");
                lbpFinalTotal = total * DEFAULT_EXCHANGE_RATE;
            }

            // Bold, larger font for the total in LBP
            var totalRow = new TableRow();
            totalRow.Cells.Add(new TableCell(new Paragraph(new Run("TOTAL:"))
            {
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Left
            }));

            totalRow.Cells.Add(new TableCell(new Paragraph(new Run($"{lbpFinalTotal:N0} LBP"))
            {
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right
            }));

            totalsTable.RowGroups[0].Rows.Add(totalRow);

            flowDocument.Blocks.Add(totalsTable);
            flowDocument.Blocks.Add(CreateDivider());

            // 5. Footer Section - Now using custom footer text from business settings
            var footer = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 5, 0, 3)
            };

            // First footer text (primary message)
            if (!string.IsNullOrWhiteSpace(footerText1))
            {
                footer.Inlines.Add(new Run(footerText1)
                {
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                });
                footer.Inlines.Add(new LineBreak());
            }

            // Second footer text (secondary message)
            if (!string.IsNullOrWhiteSpace(footerText2))
            {
                footer.Inlines.Add(new Run(footerText2)
                {
                    FontSize = 10,
                    FontWeight = FontWeights.Normal
                });
            }

            flowDocument.Blocks.Add(footer);

            return flowDocument;
        }

        private TableCell CreateCell(string text, FontWeight fontWeight = default, TextAlignment alignment = TextAlignment.Left)
        {
            var paragraph = new Paragraph(new Run(text ?? string.Empty))
            {
                FontWeight = fontWeight == default ? FontWeights.Normal : fontWeight, // Default to normal if not specified
                TextAlignment = alignment
            };
            return new TableCell(paragraph);
        }

        private void AddMetaRow(Table table, string label, string value)
        {
            if (table == null) return;

            var row = new TableRow();
            row.Cells.Add(CreateCell(label, FontWeights.Bold)); // Make label bold
            row.Cells.Add(CreateCell(value ?? string.Empty, FontWeights.Normal)); // Use normal for values
            table.RowGroups[0].Rows.Add(row);
        }

        private void AddTotalRow(Table table, string label, string value, bool isBold = false)
        {
            if (table == null) return;

            var row = new TableRow();
            var fontWeight = isBold ? FontWeights.Bold : FontWeights.Normal;
            row.Cells.Add(CreateCell(label, fontWeight, TextAlignment.Left));
            row.Cells.Add(CreateCell(value, fontWeight, TextAlignment.Right));
            table.RowGroups[0].Rows.Add(row);
        }

        private BlockUIContainer CreateDivider()
        {
            return new BlockUIContainer(new Border
            {
                Height = 1, // Reduced from 2
                Background = Brushes.Black,
                Margin = new Thickness(0, 2, 0, 2) // Reduced margins
            });
        }
    }
}