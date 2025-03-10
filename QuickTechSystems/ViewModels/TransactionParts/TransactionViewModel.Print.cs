using System;
using System.Linq;
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
            await ExecuteOperationSafelyAsync(async () =>
            {
                if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
                {
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show("No transaction to print", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                // Get the latest transaction ID before printing
                int transactionId = await _transactionService.GetLatestTransactionIdAsync();

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

                    // 1. Header Section
                    var header = new Paragraph
                    {
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Navy
                    };
                    header.Inlines.Add("سوبرماركت هادي بلحص\n");
                    header.Inlines.Add(new Run("81052944")
                    { FontSize = 14, FontWeight = FontWeights.Normal });
                    flowDocument.Blocks.Add(header);
                    flowDocument.Blocks.Add(CreateDivider());

                    // 2. Transaction Metadata
                    var metaTable = new Table { FontSize = 11.5, CellSpacing = 0 };
                    metaTable.Columns.Add(new TableColumn { Width = new GridLength(120) });
                    metaTable.Columns.Add(new TableColumn { Width = GridLength.Auto });
                    metaTable.RowGroups.Add(new TableRowGroup());
                    AddMetaRow(metaTable, "TRX #:", transactionId.ToString());  // Use the retrieved ID
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

                    foreach (var item in CurrentTransaction.Details)
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

                    // 4. Totals Section
                    var totalsTable = new Table { FontSize = 12, CellSpacing = 0 };
                    totalsTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
                    totalsTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
                    totalsTable.RowGroups.Add(new TableRowGroup());
                    AddTotalRow(totalsTable, "SUBTOTAL:", SubTotal.ToString("C2"));
                    if (DiscountAmount > 0)
                        AddTotalRow(totalsTable, "DISCOUNT:", $"-{DiscountAmount:C2}");
                    AddTotalRow(totalsTable, "TOTAL:", TotalAmount.ToString("C2"), true);

                    flowDocument.Blocks.Add(totalsTable);
                    flowDocument.Blocks.Add(CreateDivider());

                    // Print document
                    printDialog.PrintDocument(
                        ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                        "Transaction Receipt");
                });
            }, "Printing receipt", "PrintOperation");
        }

        // Helper methods
        private TableCell CreateCell(string text, FontWeight fontWeight = default, TextAlignment alignment = TextAlignment.Left)
        {
            return new TableCell(new Paragraph(new Run(text))
            {
                FontWeight = fontWeight,
                TextAlignment = alignment
            });
        }

        private void AddMetaRow(Table table, string label, string value)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(label, FontWeights.Bold));
            row.Cells.Add(CreateCell(value));
            table.RowGroups[0].Rows.Add(row);
        }

        private void AddTotalRow(Table table, string label, string value, bool isBold = false)
        {
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