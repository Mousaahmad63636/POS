// QuickTechSystems/Helpers/BarcodeHelper.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.DTOs;
using System.Printing;
using System.Windows.Markup;
using System.Diagnostics;

namespace QuickTechSystems.Helpers
{
    public class BarcodeHelper
    {
        // Label size in cm - ensure these match your actual label dimensions
        private const double LabelWidthCm = 6.0;
        private const double LabelHeightCm = 4.0;

        // Convert cm to device-independent units (1 inch = 2.54 cm, 1 inch = 96 units)
        private static readonly double LabelWidthDiu = LabelWidthCm * 96 / 2.54;
        private static readonly double LabelHeightDiu = LabelHeightCm * 96 / 2.54;

        public static FixedDocument CreateBarcodeDocument(IEnumerable<ProductDTO> products, int labelsPerProduct)
        {
            var document = new FixedDocument();
            Debug.WriteLine($"Creating barcode document with dimensions: {LabelWidthDiu}x{LabelHeightDiu} DIU");

            foreach (var product in products)
            {
                for (int i = 0; i < labelsPerProduct; i++)
                {
                    var pageContent = new PageContent();
                    var fixedPage = new FixedPage
                    {
                        Width = LabelWidthDiu,
                        Height = LabelHeightDiu
                    };

                    var barcodeLabel = CreateSimpleBarcodeLabel(product);
                    fixedPage.Children.Add(barcodeLabel);

                    ((IAddChild)pageContent).AddChild(fixedPage);
                    document.Pages.Add(pageContent);
                }
            }

            return document;
        }

        private static UIElement CreateSimpleBarcodeLabel(ProductDTO product)
        {
            var rootGrid = new Grid
            {
                Width = LabelWidthDiu,
                Height = LabelHeightDiu,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.White
            };

            var viewbox = new Viewbox
            {
                Stretch = Stretch.Fill,
                StretchDirection = StretchDirection.Both,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5)
            };

            // 1. Barcode Image
            if (product.BarcodeImage != null)
            {
                BitmapImage barcodeImage = null;
                using (var ms = new MemoryStream(product.BarcodeImage))
                {
                    barcodeImage = new BitmapImage();
                    barcodeImage.BeginInit();
                    barcodeImage.CacheOption = BitmapCacheOption.OnLoad;
                    barcodeImage.StreamSource = ms;
                    barcodeImage.EndInit();
                    barcodeImage.Freeze();
                }

                var image = new Image
                {
                    Source = barcodeImage,
                    Stretch = Stretch.Fill,
                    Width = 250,
                    Height = 80,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                stackPanel.Children.Add(image);
            }

            // 2. Barcode Text
            var barcodeText = new TextBlock
            {
                Text = product.Barcode,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };
            stackPanel.Children.Add(barcodeText);

            // 3. Product Name
            var nameText = new TextBlock
            {
                Text = TruncateText(product.Name, 30),
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stackPanel.Children.Add(nameText);

            viewbox.Child = stackPanel;
            rootGrid.Children.Add(viewbox);

            return rootGrid;
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3) + "...";
        }

        public static void PrintBarcodes(IEnumerable<ProductDTO> products, int labelsPerProduct)
        {
            var printDialog = new PrintDialog();

            if (printDialog.ShowDialog() == true)
            {
                try
                {
                    Debug.WriteLine($"Setting up printer for labels: {LabelWidthCm}cm × {LabelHeightCm}cm");

                    // Set exact media size for the label
                    printDialog.PrintTicket.PageMediaSize = new PageMediaSize(
                        LabelWidthCm / 2.54, // Convert cm to inches
                        LabelHeightCm / 2.54
                    );

                    // Disable scaling
                    printDialog.PrintTicket.PageScalingFactor = 100;

                    // Create document with simplified layout
                    var document = CreateBarcodeDocument(products, labelsPerProduct);

                    // Print with descriptive job name
                    printDialog.PrintDocument(document.DocumentPaginator, "Product Barcodes");

                    Debug.WriteLine("Print job sent successfully");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Printing error: {ex.Message}");
                    MessageBox.Show($"Error printing barcodes: {ex.Message}", "Print Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}