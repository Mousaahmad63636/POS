// QuickTechSystems.Infrastructure/Services/BarcodeService.cs
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using QuickTechSystems.Application.Services.Interfaces;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace QuickTechSystems.Infrastructure.Services
{
    public class BarcodeService : IBarcodeService
    {
        public byte[] GenerateBarcode(string barcodeData)
        {
            return GenerateBarcode(barcodeData, 300, 100);
        }

        public byte[] GenerateBarcode(string barcodeData, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(barcodeData))
                return null;

            try
            {
                // Create a barcode writer that writes to a bitmap
                var writer = new BarcodeWriter
                {
                    Format = BarcodeFormat.CODE_128,
                    Options = new EncodingOptions
                    {
                        Width = width,
                        Height = height,
                        Margin = 5
                    }
                };

                // Generate the barcode
                using (var bitmap = writer.Write(barcodeData))
                using (var memoryStream = new MemoryStream())
                {
                    // Save bitmap to memory stream as PNG
                    bitmap.Save(memoryStream, ImageFormat.Png);
                    return memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating barcode: {ex.Message}");
                return null;
            }
        }
    }
}