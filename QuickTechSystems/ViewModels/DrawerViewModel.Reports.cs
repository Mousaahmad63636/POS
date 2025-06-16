using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Printing;
using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.IO;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class DrawerViewModel
    {
        private async Task PrintReportAsync()
        {
            if (CurrentDrawer == null)
            {
                await ShowErrorMessageAsync("No active drawer found.");
                return;
            }

            await ExecuteOperationSafelyAsync(async () =>
            {
                bool printCancelled = false;
                StatusMessage = "Preparing drawer report...";
                OnPropertyChanged(nameof(StatusMessage));

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
                        var flowDocument = CreateDrawerReportDocument(
                            printDialog,
                            companyName,
                            address,
                            phoneNumber,
                            email,
                            footerText1,
                            footerText2,
                            logoPath);

                        try
                        {
                            StatusMessage = "Printing...";
                            OnPropertyChanged(nameof(StatusMessage));

                            printDialog.PrintDocument(
                                ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                                "Drawer Report");

                            StatusMessage = "Drawer report printed successfully";
                            OnPropertyChanged(nameof(StatusMessage));
                        }
                        catch (Exception printEx)
                        {
                            Debug.WriteLine($"Error during print execution: {printEx.Message}");
                            MessageBox.Show(
                                "Error printing drawer report. Please check printer connection and try again.",
                                "Print Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            StatusMessage = "Print error - Report not printed";
                            OnPropertyChanged(nameof(StatusMessage));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error preparing drawer report: {ex.Message}");
                        MessageBox.Show(
                            "An error occurred while preparing the report. Please try again.",
                            "Print Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Error preparing report";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                });

                if (printCancelled)
                {
                    StatusMessage = "Printing was cancelled";
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }, "Printing drawer report", "PrintOperation");
        }

        private FlowDocument CreateDrawerReportDocument(
            PrintDialog printDialog,
            string companyName,
            string address,
            string phoneNumber,
            string email,
            string footerText1,
            string footerText2,
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

            var cashierParagraph = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            };

            cashierParagraph.Inlines.Add(new Run("تقرير الصندوق:")
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });
            cashierParagraph.Inlines.Add(new Run(" "));
            cashierParagraph.Inlines.Add(new Run(CurrentDrawer?.CashierName ?? "غير محدد")
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });

            flowDocument.Blocks.Add(cashierParagraph);
            flowDocument.Blocks.Add(CreateDivider());

            var metaTable = new Table { FontSize = 11, CellSpacing = 0 };
            metaTable.Columns.Add(new TableColumn { Width = new GridLength(100) });
            metaTable.Columns.Add(new TableColumn { Width = GridLength.Auto });
            metaTable.RowGroups.Add(new TableRowGroup());

            if (CurrentDrawer != null)
            {
                AddMetaRow(metaTable, "رقم الصندوق:", CurrentDrawer.DrawerId.ToString());
                AddMetaRow(metaTable, "تاريخ الفتح:", CurrentDrawer.OpenedAt.ToString("MM/dd/yyyy hh:mm tt"));
                if (CurrentDrawer.ClosedAt.HasValue)
                {
                    AddMetaRow(metaTable, "تاريخ الإغلاق:", CurrentDrawer.ClosedAt.Value.ToString("MM/dd/yyyy hh:mm tt"));
                }

                var sessionDuration = CurrentDrawer.ClosedAt.HasValue
                    ? CurrentDrawer.ClosedAt.Value - CurrentDrawer.OpenedAt
                    : DateTime.Now - CurrentDrawer.OpenedAt;
                AddMetaRow(metaTable, "مدة الجلسة:", $"{sessionDuration.Days}ي {sessionDuration.Hours}س {sessionDuration.Minutes}د");
            }

            AddMetaRow(metaTable, "تاريخ التقرير:", DateTime.Now.ToString("MM/dd/yyyy hh:mm tt"));

            flowDocument.Blocks.Add(metaTable);
            flowDocument.Blocks.Add(CreateDivider());

            var financialTable = new Table { FontSize = 12, CellSpacing = 0 };
            financialTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            financialTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
            financialTable.RowGroups.Add(new TableRowGroup());

            var financialHeaderRow = new TableRow { Background = Brushes.LightGray };
            var headerCell = new TableCell(new Paragraph(new Bold(new Run("الملخص المالي")))
            {
                TextAlignment = TextAlignment.Center,
                FontSize = 12
            });
            headerCell.ColumnSpan = 2;
            financialHeaderRow.Cells.Add(headerCell);
            financialTable.RowGroups[0].Rows.Add(financialHeaderRow);

            if (CurrentDrawer != null)
            {
                AddTotalRow(financialTable, "الرصيد الافتتاحي:", $"{CurrentDrawer.OpeningBalance:F0}");
                AddTotalRow(financialTable, "إجمالي المبيعات:", $"{GetSessionTotalSales():F0}");
                AddTotalRow(financialTable, "إجمالي النقد الداخل:", $"{GetSessionTotalCashIn():F0}");
                AddTotalRow(financialTable, "إجمالي النقد الخارج:", $"{GetSessionTotalCashOut():F0}");
                AddTotalRow(financialTable, "إجمالي المصروفات:", $"{GetSessionTotalExpenses():F0}");

                var finalTotal = CurrentDrawer.OpeningBalance + GetSessionTotalSales() + GetSessionTotalCashIn() - GetSessionTotalExpenses() - GetSessionTotalCashOut();

                var totalRow = new TableRow();
                totalRow.Cells.Add(new TableCell(new Paragraph(new Run("إجمالي الصندوق النهائي:"))
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

                totalParagraph.Inlines.Add(new Run($"{finalTotal:F0}")
                {
                    Foreground = Brushes.Black
                });

                totalCell.Blocks.Add(totalParagraph);
                totalRow.Cells.Add(totalCell);
                financialTable.RowGroups[0].Rows.Add(totalRow);

                AddTotalRow(financialTable, "الرصيد الفعلي:", $"{CurrentDrawer.CurrentBalance:F0}");

                var difference = CurrentDrawer.CurrentBalance - finalTotal;
                if (Math.Abs(difference) > 0.01m)
                {
                    var differenceRow = new TableRow();
                    differenceRow.Cells.Add(new TableCell(new Paragraph(new Run("الفرق:"))
                    {
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Left
                    }));

                    var differenceCell = new TableCell();
                    var differenceParagraph = new Paragraph
                    {
                        TextAlignment = TextAlignment.Right,
                        FontSize = 13,
                        FontWeight = FontWeights.Bold
                    };

                    differenceParagraph.Inlines.Add(new Run($"{difference:F0}")
                    {
                        Foreground = difference < 0 ? Brushes.Red : Brushes.Green
                    });

                    differenceCell.Blocks.Add(differenceParagraph);
                    differenceRow.Cells.Add(differenceCell);
                    financialTable.RowGroups[0].Rows.Add(differenceRow);
                }
            }

            flowDocument.Blocks.Add(financialTable);
            flowDocument.Blocks.Add(CreateDivider());

            if (DrawerHistory.Any())
            {
                var transactionsTable = new Table { FontSize = 11, CellSpacing = 0 };

                transactionsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
                transactionsTable.Columns.Add(new TableColumn { Width = new GridLength(1.5, GridUnitType.Star) });
                transactionsTable.Columns.Add(new TableColumn { Width = new GridLength(1.5, GridUnitType.Star) });
                transactionsTable.Columns.Add(new TableColumn { Width = new GridLength(1.5, GridUnitType.Star) });

                transactionsTable.RowGroups.Add(new TableRowGroup());

                var headerRow = new TableRow { Background = Brushes.LightGray };
                headerRow.Cells.Add(CreateCell("التاريخ/الوقت", FontWeights.Bold, TextAlignment.Center));
                headerRow.Cells.Add(CreateCell("النوع", FontWeights.Bold, TextAlignment.Center));
                headerRow.Cells.Add(CreateCell("المبلغ", FontWeights.Bold, TextAlignment.Right));
                headerRow.Cells.Add(CreateCell("الرصيد", FontWeights.Bold, TextAlignment.Right));
                transactionsTable.RowGroups[0].Rows.Add(headerRow);

                foreach (var transaction in DrawerHistory.OrderBy(t => t.Timestamp).Take(15))
                {
                    var row = new TableRow();

                    row.Cells.Add(CreateCell(transaction.Timestamp.ToString("MM/dd HH:mm"), FontWeights.Normal, TextAlignment.Center));
                    row.Cells.Add(CreateCell(GetArabicTransactionType(transaction.Type), FontWeights.Normal, TextAlignment.Center));
                    row.Cells.Add(CreateCell($"{transaction.Amount:F0}", FontWeights.Normal, TextAlignment.Right));
                    row.Cells.Add(CreateCell($"{transaction.Balance:F0}", FontWeights.Normal, TextAlignment.Right));
                    transactionsTable.RowGroups[0].Rows.Add(row);
                }

                if (DrawerHistory.Count() > 15)
                {
                    var moreRow = new TableRow();
                    var moreCell = new TableCell(new Paragraph(new Run($"... و {DrawerHistory.Count() - 15} معاملة أخرى"))
                    {
                        TextAlignment = TextAlignment.Center,
                        FontStyle = FontStyles.Italic
                    });
                    moreCell.ColumnSpan = 4;
                    moreRow.Cells.Add(moreCell);
                    transactionsTable.RowGroups[0].Rows.Add(moreRow);
                }

                flowDocument.Blocks.Add(transactionsTable);
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

        private string GetArabicTransactionType(string type)
        {
            return type?.ToLower() switch
            {
                "open" => "فتح",
                "close" => "إغلاق",
                "cash sale" => "مبيعات نقدية",
                "cash in" => "إدخال نقد",
                "cash out" => "إخراج نقد",
                "expense" => "مصروف",
                "supplier payment" => "دفع مورد",
                "salary withdrawal" => "سحب راتب",
                "return" => "مرتجع",
                "cash receipt" => "إيصال نقدي",
                _ => type ?? ""
            };
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

        private decimal GetSessionTotalCashIn()
        {
            if (CurrentDrawer == null) return 0;

            var endDate = CurrentDrawer.ClosedAt ?? DateTime.Now;
            return DrawerHistory
                .Where(t => t.Timestamp >= CurrentDrawer.OpenedAt &&
                           t.Timestamp <= endDate &&
                           t.Type?.Equals("Cash In", StringComparison.OrdinalIgnoreCase) == true)
                .Sum(t => Math.Abs(t.Amount));
        }

        private decimal GetSessionTotalCashOut()
        {
            if (CurrentDrawer == null) return 0;

            var endDate = CurrentDrawer.ClosedAt ?? DateTime.Now;
            return DrawerHistory
                .Where(t => t.Timestamp >= CurrentDrawer.OpenedAt &&
                           t.Timestamp <= endDate &&
                           t.Type?.Equals("Cash Out", StringComparison.OrdinalIgnoreCase) == true)
                .Sum(t => Math.Abs(t.Amount));
        }


        private decimal GetSessionTotalSales()
        {
            if (CurrentDrawer == null) return 0;

            var endDate = CurrentDrawer.ClosedAt ?? DateTime.Now;
            var sessionTransactions = DrawerHistory
                .Where(t => t.Timestamp >= CurrentDrawer.OpenedAt && t.Timestamp <= endDate)
                .ToList();

            var regularSales = sessionTransactions
                .Where(t => t.Type?.Equals("Cash Sale", StringComparison.OrdinalIgnoreCase) == true &&
                           t.ActionType != "Transaction Modification")
                .Sum(t => Math.Abs(t.Amount));

            var salesModifications = sessionTransactions
                .Where(t => t.Type?.Equals("Cash Sale", StringComparison.OrdinalIgnoreCase) == true &&
                           t.ActionType == "Transaction Modification")
                .Sum(t => t.Amount);

            var debtPayments = sessionTransactions
                .Where(t => (t.Type?.Equals("Cash Receipt", StringComparison.OrdinalIgnoreCase) == true) ||
                           (t.ActionType?.Equals("Increase", StringComparison.OrdinalIgnoreCase) == true &&
                            t.Description != null && t.Description.Contains("Debt payment", StringComparison.OrdinalIgnoreCase)))
                .Sum(t => Math.Abs(t.Amount));

            return regularSales + salesModifications + debtPayments;
        }

        private decimal GetSessionTotalExpenses()
        {
            if (CurrentDrawer == null) return 0;

            var endDate = CurrentDrawer.ClosedAt ?? DateTime.Now;
            var sessionTransactions = DrawerHistory
                .Where(t => t.Timestamp >= CurrentDrawer.OpenedAt && t.Timestamp <= endDate)
                .ToList();

            var regularExpenses = sessionTransactions
                .Where(t => t.Type?.Equals("Expense", StringComparison.OrdinalIgnoreCase) == true &&
                           t.ActionType != "Transaction Modification")
                .Sum(t => Math.Abs(t.Amount)) +
                sessionTransactions
                .Where(t => t.Type?.Equals("Expense", StringComparison.OrdinalIgnoreCase) == true &&
                           t.ActionType == "Transaction Modification")
                .Sum(t => t.Amount);

            var salaryWithdrawals = sessionTransactions
                .Where(t => t.Type?.Equals("Salary Withdrawal", StringComparison.OrdinalIgnoreCase) == true)
                .Sum(t => Math.Abs(t.Amount));

            var supplierPayments = sessionTransactions
                .Where(t => t.Type?.Equals("Supplier Payment", StringComparison.OrdinalIgnoreCase) == true &&
                           t.ActionType != "Transaction Modification")
                .Sum(t => Math.Abs(t.Amount)) +
                sessionTransactions
                .Where(t => t.Type?.Equals("Supplier Payment", StringComparison.OrdinalIgnoreCase) == true &&
                           t.ActionType == "Transaction Modification")
                .Sum(t => t.Amount);

            return regularExpenses + salaryWithdrawals + supplierPayments;
        }
    }
}