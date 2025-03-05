using System;
using System.Linq;
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
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                await WindowManager.InvokeAsync(() =>
                    MessageBox.Show("No transaction to print", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            try
            {
                int transactionId = await _transactionService.GetLatestTransactionIdAsync();

                await WindowManager.InvokeAsync(async () =>
                {
                    var printDialog = new PrintDialog();
                    if (printDialog.ShowDialog() != true) return;

                    var flowDocument = new FlowDocument
                    {
                        PageWidth = printDialog.PrintableAreaWidth,
                        ColumnWidth = printDialog.PrintableAreaWidth,
                        FontFamily = new FontFamily("Arial"),
                        PagePadding = new Thickness(20, 0, 20, 0),
                        TextAlignment = TextAlignment.Center,
                        PageHeight = printDialog.PrintableAreaHeight,
                        Foreground = Brushes.Black
                    };

                    // Header Section
                    var header = new Paragraph
                    {
                        FontSize = 18, // Reduced from 22
                        FontWeight = FontWeights.ExtraBold,
                        Foreground = Brushes.Black,
                        Margin = new Thickness(0, 0, 0, 2) // Reduced margin
                    };
                    header.Inlines.Add("GalaxyNet\n");
                    header.Inlines.Add(new Run("Your partner in all your IT problems\n 81 20 77 06\n 03 65 74 64 \n ")
                    {
                        FontSize = 12, // Reduced from 16
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Black
                    });
                    flowDocument.Blocks.Add(header);
                    flowDocument.Blocks.Add(CreateDivider());

                    // Transaction Metadata
                    var metaTable = new Table { FontSize = 11, CellSpacing = 0 }; // Reduced from 13
                    metaTable.Columns.Add(new TableColumn { Width = new GridLength(120) });
                    metaTable.Columns.Add(new TableColumn { Width = GridLength.Auto });
                    metaTable.RowGroups.Add(new TableRowGroup());
                    AddMetaRow(metaTable, "TRX #:", transactionId.ToString());
                    AddMetaRow(metaTable, "DATE:", DateTime.Now.ToString("MM/dd/yyyy hh:mm tt"));
                    if (SelectedCustomer != null)
                        AddMetaRow(metaTable, "CUSTOMER:", SelectedCustomer.Name);
                    flowDocument.Blocks.Add(metaTable);
                    flowDocument.Blocks.Add(CreateDivider());

                    // Items Table
                    var itemsTable = new Table { FontSize = 11, CellSpacing = 0 }; // Reduced from 13
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(4, GridUnitType.Star) });
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
                    itemsTable.RowGroups.Add(new TableRowGroup());

                    var headerRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(220, 220, 220)) };
                    headerRow.Cells.Add(CreateCell("ITEM", FontWeights.ExtraBold, TextAlignment.Left));
                    headerRow.Cells.Add(CreateCell("QTY", FontWeights.ExtraBold, TextAlignment.Center));
                    headerRow.Cells.Add(CreateCell("PRICE", FontWeights.ExtraBold, TextAlignment.Right));
                    headerRow.Cells.Add(CreateCell("TOTAL", FontWeights.ExtraBold, TextAlignment.Right));
                    itemsTable.RowGroups[0].Rows.Add(headerRow);

                    foreach (var item in CurrentTransaction.Details)
                    {
                        var row = new TableRow();
                        row.Cells.Add(CreateCell(item.ProductName.Trim(), FontWeights.Normal, TextAlignment.Left));
                        row.Cells.Add(CreateCell(item.Quantity.ToString(), FontWeights.Normal, TextAlignment.Center));
                        row.Cells.Add(CreateCell(item.UnitPrice.ToString("C2"), FontWeights.Normal, TextAlignment.Right));
                        row.Cells.Add(CreateCell(item.Total.ToString("C2"), FontWeights.Normal, TextAlignment.Right));
                        itemsTable.RowGroups[0].Rows.Add(row);
                    }
                    flowDocument.Blocks.Add(itemsTable);
                    flowDocument.Blocks.Add(CreateDivider());

                    // Totals Section
                    var totalsTable = new Table { FontSize = 11, CellSpacing = 0 }; // Reduced from 13
                    totalsTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
                    totalsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
                    totalsTable.RowGroups.Add(new TableRowGroup());
                    AddTotalRow(totalsTable, "SUBTOTAL:", SubTotal.ToString("C2"));
                    if (DiscountAmount > 0)
                        AddTotalRow(totalsTable, "DISCOUNT:", $"-{DiscountAmount:C2}");

                    // Add USD Total
                    AddTotalRow(totalsTable, "TOTAL USD:", TotalAmount.ToString("C2"), true);

                    // Add LBP Total
                    decimal lbpAmount = CurrencyHelper.ConvertToLBP(TotalAmount);
                    AddTotalRow(totalsTable, "TOTAL LBP:", CurrencyHelper.FormatLBP(lbpAmount), true);

                    flowDocument.Blocks.Add(totalsTable);
                    flowDocument.Blocks.Add(CreateDivider());

                    printDialog.PrintDocument(
                        ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                        "Transaction Receipt");
                });
            }
            catch (Exception ex)
            {
                await WindowManager.InvokeAsync(() =>
                    MessageBox.Show($"Print Error: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        private TableCell CreateCell(string text, FontWeight fontWeight = default, TextAlignment alignment = TextAlignment.Left)
        {
            return new TableCell(new Paragraph(new Run(text)
            {
                Foreground = Brushes.Black
            })
            {
                FontWeight = fontWeight,
                TextAlignment = alignment,
                Margin = new Thickness(1) // Reduced from 2
            });
        }

        private void AddMetaRow(Table table, string label, string value)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(label, FontWeights.ExtraBold));
            row.Cells.Add(CreateCell(value, FontWeights.Bold));
            table.RowGroups[0].Rows.Add(row);
        }

        private void AddTotalRow(Table table, string label, string value, bool isBold = false)
        {
            var row = new TableRow();
            var weight = isBold ? FontWeights.ExtraBold : FontWeights.Bold;
            row.Cells.Add(CreateCell(label, weight, TextAlignment.Left));
            row.Cells.Add(CreateCell(value, weight, TextAlignment.Right));
            table.RowGroups[0].Rows.Add(row);
        }

        private BlockUIContainer CreateDivider()
        {
            return new BlockUIContainer(new Border
            {
                Height = 1, // Reduced from 2
                Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                Margin = new Thickness(0, 2, 0, 2) // Reduced from 4
            });
        }

    }
}