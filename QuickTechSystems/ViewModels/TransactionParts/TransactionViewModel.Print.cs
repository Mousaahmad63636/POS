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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Helpers;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel
    {
        private async Task PrintReceipt()
        {
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

                var transactionSnapshot = new TransactionDTO
                {
                    TransactionId = CurrentTransaction.TransactionId,
                    TransactionDate = CurrentTransaction.TransactionDate,
                    CustomerId = CurrentTransaction.CustomerId,
                    CustomerName = SelectedCustomer?.Name ?? CurrentTransaction.CustomerName,
                    TotalAmount = TotalAmount,
                    Details = new ObservableCollection<TransactionDetailDTO>()
                };

                var productCache = new Dictionary<int, ProductDTO>();

                foreach (var detail in CurrentTransaction.Details)
                {
                    var detailCopy = new TransactionDetailDTO
                    {
                        TransactionDetailId = detail.TransactionDetailId,
                        TransactionId = detail.TransactionId,
                        ProductId = detail.ProductId,
                        Quantity = detail.Quantity,
                        UnitPrice = detail.UnitPrice,
                        PurchasePrice = detail.PurchasePrice,
                        Discount = detail.Discount,
                        Total = detail.Total,
                        ProductBarcode = detail.ProductBarcode
                    };

                    if (string.IsNullOrWhiteSpace(detail.ProductName))
                    {
                        if (!productCache.TryGetValue(detail.ProductId, out var product))
                        {
                            product = await _productService.GetByIdAsync(detail.ProductId);
                            if (product != null)
                            {
                                productCache[detail.ProductId] = product;
                            }
                        }

                        detailCopy.ProductName = productCache.TryGetValue(detail.ProductId, out var cachedProduct)
                            ? cachedProduct.Name
                            : $"Product {detail.ProductId}";
                    }
                    else
                    {
                        detailCopy.ProductName = detail.ProductName;
                    }

                    transactionSnapshot.Details.Add(detailCopy);
                }

                int transactionId;
                try
                {
                    transactionId = await _transactionService.GetLatestTransactionIdAsync();
                    if (transactionId <= 0)
                    {
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

                decimal currentSubTotal = SubTotal;
                decimal currentDiscountAmount = DiscountAmount;

                string companyName;
                string address;
                string phoneNumber;
                string email;
                string footerText1;
                string footerText2;
                string logoPath = null;

                try
                {
                    companyName = await _businessSettingsService.GetSettingValueAsync("CompanyName", "Your Business Name");
                    address = await _businessSettingsService.GetSettingValueAsync("Address", "Your Business Address");
                    phoneNumber = await _businessSettingsService.GetSettingValueAsync("Phone", "Your Phone Number");
                    email = await _businessSettingsService.GetSettingValueAsync("Email", "");
                    footerText1 = await _businessSettingsService.GetSettingValueAsync("ReceiptFooter1", "Stay caffeinated!!");
                    footerText2 = await _businessSettingsService.GetSettingValueAsync("ReceiptFooter2", "See you next time");

                    var logoSetting = await _businessSettingsService.GetByKeyAsync("CompanyLogo");
                    if (logoSetting != null && !string.IsNullOrEmpty(logoSetting.Value))
                    {
                        logoPath = logoSetting.Value;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error retrieving business settings: {ex.Message}");
                    companyName = "Your Business Name";
                    address = "Your Business Address";
                    phoneNumber = "Your Phone Number";
                    email = "";
                    footerText1 = "Stay caffeinated!!";
                    footerText2 = "See you next time";
                    logoPath = null;
                }

                decimal previousCustomerBalance = 0;
                decimal currentTransactionTotal = Math.Max(0, currentSubTotal - currentDiscountAmount);

                if (transactionSnapshot.CustomerId.HasValue && SelectedCustomer != null)
                {
                    try
                    {
                        var customer = await _customerService.GetByIdAsync(SelectedCustomer.CustomerId);
                        if (customer != null)
                        {
                            previousCustomerBalance = customer.Balance;

                            if (IsEditingTransaction || CurrentTransaction.TransactionId > 0)
                            {
                                previousCustomerBalance -= currentTransactionTotal;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error retrieving customer balance: {ex.Message}");
                        previousCustomerBalance = SelectedCustomer.Balance;
                    }
                }

                await WindowManager.InvokeAsync(async () =>
                {
                    StatusMessage = "Opening print dialog...";
                    OnPropertyChanged(nameof(StatusMessage));

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

                    PrintDialog printDialog = new PrintDialog();
                    bool? dialogResult = false;

                    try
                    {
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
                            currentDiscountAmount,
                            previousCustomerBalance,
                            logoPath);

                        try
                        {
                            StatusMessage = "Printing...";
                            OnPropertyChanged(nameof(StatusMessage));

                            printDialog.PrintDocument(
                                ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                                "Transaction Receipt");

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
            decimal discountAmount,
            decimal previousCustomerBalance,
            string logoPath = null)
        {
            var flowDocument = new FlowDocument
            {
                PageWidth = printDialog.PrintableAreaWidth,
                ColumnWidth = printDialog.PrintableAreaWidth,
                FontFamily = new FontFamily("Segoe UI, Arial"),
                FontWeight = FontWeights.Normal,
                PagePadding = new Thickness(10, 0, 10, 0),
                TextAlignment = TextAlignment.Center,
                PageHeight = printDialog.PrintableAreaHeight
            };

            var header = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };

            header.Inlines.Add(new Run(companyName)
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black
            });
            header.Inlines.Add(new LineBreak());

            try
            {
                if (!string.IsNullOrEmpty(logoPath))
                {
                    BitmapImage logo = new BitmapImage();
                    if (File.Exists(logoPath))
                    {
                        logo.BeginInit();
                        logo.CacheOption = BitmapCacheOption.OnLoad;
                        logo.UriSource = new Uri(logoPath);
                        logo.EndInit();
                        logo.Freeze();
                    }
                    else
                    {
                        try
                        {
                            byte[] logoBytes = Convert.FromBase64String(logoPath);
                            using (MemoryStream ms = new MemoryStream(logoBytes))
                            {
                                logo.BeginInit();
                                logo.CacheOption = BitmapCacheOption.OnLoad;
                                logo.StreamSource = ms;
                                logo.EndInit();
                                logo.Freeze();
                            }
                        }
                        catch
                        {
                            LoadDefaultLogo(header);
                        }
                    }

                    if (logo.IsDownloading == false && logo.Width > 0)
                    {
                        Image logoImage = new Image
                        {
                            Source = logo,
                            Width = 150,
                            Height = 70,
                            Stretch = Stretch.Uniform,
                            Margin = new Thickness(0, 5, 0, 5)
                        };

                        header.Inlines.Add(new InlineUIContainer(logoImage));
                        header.Inlines.Add(new LineBreak());
                    }
                }
                else
                {
                    LoadDefaultLogo(header);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading logo image: {ex.Message}");
                LoadDefaultLogo(header);
            }

            if (!string.IsNullOrWhiteSpace(address))
            {
                header.Inlines.Add(new Run(address)
                {
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                });
                header.Inlines.Add(new LineBreak());
            }

            if (!string.IsNullOrWhiteSpace(phoneNumber))
            {
                header.Inlines.Add(new Run(phoneNumber)
                {
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                });
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                header.Inlines.Add(new LineBreak());
                header.Inlines.Add(new Run(email)
                {
                    FontSize = 11,
                    FontWeight = FontWeights.Normal
                });
            }

            flowDocument.Blocks.Add(header);

            string customerDisplay = "زائر";
            if (!string.IsNullOrEmpty(transaction.CustomerName))
            {
                customerDisplay = transaction.CustomerName;
            }
            else if (transaction.CustomerId.HasValue)
            {
                customerDisplay = $"رقم المعرف: {transaction.CustomerId}";
            }

            var customerParagraph = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            };

            customerParagraph.Inlines.Add(new Run("الساده:")
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });
            customerParagraph.Inlines.Add(new Run(" "));
            customerParagraph.Inlines.Add(new Run(customerDisplay)
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });

            flowDocument.Blocks.Add(customerParagraph);
            flowDocument.Blocks.Add(CreateDivider());

            var metaTable = new Table { FontSize = 11, CellSpacing = 0 };
            metaTable.Columns.Add(new TableColumn { Width = new GridLength(100) });
            metaTable.Columns.Add(new TableColumn { Width = GridLength.Auto });
            metaTable.RowGroups.Add(new TableRowGroup());
            AddMetaRow(metaTable, "رقم العملية:", transactionId.ToString());
            AddMetaRow(metaTable, "التاريخ:", DateTime.Now.ToString("MM/dd/yyyy hh:mm tt"));

            flowDocument.Blocks.Add(metaTable);
            flowDocument.Blocks.Add(CreateDivider());

            var itemsTable = new Table { FontSize = 11, CellSpacing = 0 };

            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1.5, GridUnitType.Star) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1.5, GridUnitType.Star) });

            itemsTable.RowGroups.Add(new TableRowGroup());

            var headerRow = new TableRow { Background = Brushes.LightGray };
            headerRow.Cells.Add(CreateCell("المنتج", FontWeights.Bold, TextAlignment.Left));
            headerRow.Cells.Add(CreateCell("الكمية", FontWeights.Bold, TextAlignment.Center));
            headerRow.Cells.Add(CreateCell("السعر", FontWeights.Bold, TextAlignment.Right));
            headerRow.Cells.Add(CreateCell("المجموع", FontWeights.Bold, TextAlignment.Right));
            itemsTable.RowGroups[0].Rows.Add(headerRow);

            if (transaction?.Details != null && transaction.Details.Any())
            {
                foreach (var item in transaction.Details)
                {
                    var row = new TableRow();

                    row.Cells.Add(CreateCell(item.ProductName, FontWeights.Normal, TextAlignment.Left));
                    row.Cells.Add(CreateCell(item.Quantity.ToString(), FontWeights.Normal, TextAlignment.Center));
                    row.Cells.Add(CreateCell($"${item.UnitPrice:N2}", FontWeights.Normal, TextAlignment.Right));
                    row.Cells.Add(CreateCell($"${item.Total:N2}", FontWeights.Normal, TextAlignment.Right));
                    itemsTable.RowGroups[0].Rows.Add(row);
                }
            }
            else
            {
                var emptyRow = new TableRow();
                emptyRow.Cells.Add(CreateCell("لا توجد منتجات", FontWeights.Normal, TextAlignment.Center));
                emptyRow.Cells.Add(CreateCell("", FontWeights.Normal, TextAlignment.Center));
                emptyRow.Cells.Add(CreateCell("", FontWeights.Normal, TextAlignment.Center));
                emptyRow.Cells.Add(CreateCell("", FontWeights.Normal, TextAlignment.Center));
                itemsTable.RowGroups[0].Rows.Add(emptyRow);
            }

            flowDocument.Blocks.Add(itemsTable);
            flowDocument.Blocks.Add(CreateDivider());

            var totalsTable = new Table { FontSize = 12, CellSpacing = 0 };
            totalsTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            totalsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
            totalsTable.RowGroups.Add(new TableRowGroup());

            AddTotalRow(totalsTable, "المجموع الفرعي:", $"${subTotal:N2}");

            if (discountAmount > 0)
            {
                AddTotalRow(totalsTable, "الخصم:", $"-${discountAmount:N2}");
            }

            decimal total = Math.Max(0, subTotal - discountAmount);

            // Calculate total in LBP
            decimal totalLBP;
            try
            {
                totalLBP = CurrencyHelper.RoundLBP(CurrencyHelper.ConvertToLBP(total));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting total to LBP: {ex.Message}");
                totalLBP = CurrencyHelper.RoundLBP(total * 100000m);
            }

            var totalRow = new TableRow();
            totalRow.Cells.Add(new TableCell(new Paragraph(new Run("المجموع:"))
            {
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Left
            }));

            var totalCell = new TableCell();
            var totalParagraph = new Paragraph
            {
                TextAlignment = TextAlignment.Right,
                FontSize = 13,
                FontWeight = FontWeights.Bold
            };

            totalParagraph.Inlines.Add(new Run($"${total:N2}")
            {
                Foreground = Brushes.Black
            });
            totalParagraph.Inlines.Add(new LineBreak());
            totalParagraph.Inlines.Add(new Run($"{totalLBP:N0} LBP")
            {
                Foreground = Brushes.Black,
                FontSize = 12
            });

            totalCell.Blocks.Add(totalParagraph);
            totalRow.Cells.Add(totalCell);

            totalsTable.RowGroups[0].Rows.Add(totalRow);

            flowDocument.Blocks.Add(totalsTable);
            flowDocument.Blocks.Add(CreateDivider());

            if (transaction.CustomerId.HasValue && (previousCustomerBalance > 0 || total > 0))
            {
                var balanceTable = new Table { FontSize = 12, CellSpacing = 0 };
                balanceTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
                balanceTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
                balanceTable.RowGroups.Add(new TableRowGroup());

                var balanceHeaderRow = new TableRow { Background = Brushes.LightGray };
                var headerCell = new TableCell(new Paragraph(new Bold(new Run("حساب الزبون")))
                {
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12
                });
                headerCell.ColumnSpan = 2;
                balanceHeaderRow.Cells.Add(headerCell);
                balanceTable.RowGroups[0].Rows.Add(balanceHeaderRow);

                decimal newBalance = previousCustomerBalance + total;

                // Display customer balances in USD
                AddTotalRow(balanceTable, "الرصيد السابق:", $"${previousCustomerBalance:N2}");
                AddTotalRow(balanceTable, "المشتريات الحالية:", $"${total:N2}");

                var totalBalanceRow = new TableRow();
                totalBalanceRow.Cells.Add(new TableCell(new Paragraph(new Run("الرصيد الإجمالي الجديد:"))
                {
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Left
                }));

                var balanceCell = new TableCell();
                var balanceParagraph = new Paragraph
                {
                    TextAlignment = TextAlignment.Right,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold
                };

                // Display the new balance in USD
                balanceParagraph.Inlines.Add(new Run($"${newBalance:N2}")
                {
                    Foreground = Brushes.DarkRed
                });

                // Display the new balance in LBP on a new line
                decimal newBalanceLBP = CurrencyHelper.RoundLBP(CurrencyHelper.ConvertToLBP(newBalance));
                balanceParagraph.Inlines.Add(new LineBreak());
                balanceParagraph.Inlines.Add(new Run($"{newBalanceLBP:N0} LBP")
                {
                    Foreground = Brushes.DarkRed,
                    FontSize = 12
                });

                balanceCell.Blocks.Add(balanceParagraph);
                totalBalanceRow.Cells.Add(balanceCell);

                balanceTable.RowGroups[0].Rows.Add(totalBalanceRow);

                flowDocument.Blocks.Add(balanceTable);
                flowDocument.Blocks.Add(CreateDivider());
            }

            var footer = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 5, 0, 3)
            };

            if (!string.IsNullOrWhiteSpace(footerText1))
            {
                footer.Inlines.Add(new Run(footerText1)
                {
                    FontSize = 13,
                    FontWeight = FontWeights.Bold
                });
                footer.Inlines.Add(new LineBreak());
            }

            if (!string.IsNullOrWhiteSpace(footerText2))
            {
                footer.Inlines.Add(new Run(footerText2)
                {
                    FontSize = 11,
                    FontWeight = FontWeights.Normal
                });
            }

            flowDocument.Blocks.Add(footer);

            return flowDocument;
        }

        private void LoadDefaultLogo(Paragraph header)
        {
            try
            {
                BitmapImage logo = new BitmapImage();
                logo.BeginInit();
                logo.UriSource = new Uri("pack://application:,,,/Resources/Images/Logo.png");
                logo.CacheOption = BitmapCacheOption.OnLoad;
                logo.EndInit();
                logo.Freeze();

                Image logoImage = new Image
                {
                    Source = logo,
                    Width = 150,
                    Height = 70,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 5, 0, 5)
                };

                header.Inlines.Add(new InlineUIContainer(logoImage));
                header.Inlines.Add(new LineBreak());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading default logo image: {ex.Message}");
            }
        }

        private TableCell CreateCell(string text, FontWeight fontWeight = default, TextAlignment alignment = TextAlignment.Left)
        {
            var paragraph = new Paragraph(new Run(text ?? string.Empty))
            {
                FontWeight = fontWeight == default ? FontWeights.Normal : fontWeight,
                TextAlignment = alignment,
                Margin = new Thickness(2)
            };
            return new TableCell(paragraph);
        }

        private void AddMetaRow(Table table, string label, string value)
        {
            if (table == null) return;

            var row = new TableRow();
            row.Cells.Add(CreateCell(label, FontWeights.Bold));
            row.Cells.Add(CreateCell(value ?? string.Empty, FontWeights.Normal));
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
                Height = 1,
                Background = Brushes.Black,
                Margin = new Thickness(0, 2, 0, 2)
            });
        }
    }
}