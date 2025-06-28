using System;
using System.Collections.Generic;
using System.Linq;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Helpers
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public string ErrorSummary => string.Join(Environment.NewLine, Errors);
    }

    public static class ProductValidationHelper
    {
        public static ValidationResult ValidateInvoiceDetail(SupplierInvoiceDetailDTO detail)
        {
            var result = new ValidationResult { IsValid = true };

            // Product name validation
            if (string.IsNullOrWhiteSpace(detail.ProductName))
            {
                result.Errors.Add("Product name is required.");
                result.IsValid = false;
            }
            else if (detail.ProductName.Length > 200)
            {
                result.Errors.Add("Product name cannot exceed 200 characters.");
                result.IsValid = false;
            }

            // Barcode validation
            if (string.IsNullOrWhiteSpace(detail.ProductBarcode))
            {
                result.Errors.Add("Product barcode is required.");
                result.IsValid = false;
            }

            // Quantity validation
            if (detail.Quantity <= 0)
            {
                result.Errors.Add("Quantity must be greater than zero.");
                result.IsValid = false;
            }

            // Price validation
            if (detail.PurchasePrice <= 0)
            {
                result.Errors.Add("Purchase price must be greater than zero.");
                result.IsValid = false;
            }

            // Items per box validation
            if (detail.ItemsPerBox <= 0)
            {
                result.Errors.Add("Items per box must be greater than zero.");
                result.IsValid = false;
            }

            // Box quantity validation
            if (detail.NumberOfBoxes < 0)
            {
                result.Errors.Add("Number of boxes cannot be negative.");
                result.IsValid = false;
            }

            // Logical validation - quantity should match boxes * items per box
            var expectedQuantity = detail.NumberOfBoxes * detail.ItemsPerBox;
            if (Math.Abs(detail.Quantity - expectedQuantity) > 0.01m && detail.NumberOfBoxes > 0)
            {
                result.Errors.Add($"Quantity ({detail.Quantity}) should equal Boxes ({detail.NumberOfBoxes}) × Items per Box ({detail.ItemsPerBox}) = {expectedQuantity}.");
                result.IsValid = false;
            }

            return result;
        }

        public static ValidationResult ValidateInvoiceDetails(IEnumerable<SupplierInvoiceDetailDTO> details)
        {
            var result = new ValidationResult { IsValid = true };
            var barcodes = new HashSet<string>();
            var productNames = new HashSet<string>();
            int rowNumber = 1;

            foreach (var detail in details)
            {
                // Validate individual detail
                var detailResult = ValidateInvoiceDetail(detail);
                if (!detailResult.IsValid)
                {
                    foreach (var error in detailResult.Errors)
                    {
                        result.Errors.Add($"Row {rowNumber}: {error}");
                    }
                    result.IsValid = false;
                }

                // Check for duplicate barcodes
                if (!string.IsNullOrWhiteSpace(detail.ProductBarcode))
                {
                    if (barcodes.Contains(detail.ProductBarcode))
                    {
                        result.Errors.Add($"Row {rowNumber}: Duplicate barcode '{detail.ProductBarcode}' found in invoice.");
                        result.IsValid = false;
                    }
                    else
                    {
                        barcodes.Add(detail.ProductBarcode);
                    }
                }

                // Check for duplicate product names (warning, not error)
                if (!string.IsNullOrWhiteSpace(detail.ProductName))
                {
                    if (productNames.Contains(detail.ProductName.ToLower()))
                    {
                        result.Errors.Add($"Row {rowNumber}: Warning - Product name '{detail.ProductName}' appears multiple times.");
                        // Don't set IsValid to false for this warning
                    }
                    else
                    {
                        productNames.Add(detail.ProductName.ToLower());
                    }
                }

                rowNumber++;
            }

            return result;
        }

        public static bool IsValidBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return false;

            // Basic barcode validation - adjust according to your barcode standards
            return barcode.Length >= 8 && barcode.Length <= 20 &&
                   barcode.All(c => char.IsDigit(c) || char.IsLetter(c));
        }

        public static decimal CalculateLineTotal(SupplierInvoiceDetailDTO detail)
        {
            return detail.Quantity * detail.PurchasePrice;
        }

        public static void UpdateCalculatedFields(SupplierInvoiceDetailDTO detail)
        {
            // Update total price
            detail.TotalPrice = CalculateLineTotal(detail);

            // Update quantity from boxes if boxes are specified
            if (detail.NumberOfBoxes > 0 && detail.ItemsPerBox > 0)
            {
                detail.Quantity = detail.NumberOfBoxes * detail.ItemsPerBox;
                detail.TotalPrice = CalculateLineTotal(detail);
            }
        }
    }
}