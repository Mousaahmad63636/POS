using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Windows.Markup;
using System.Text;

namespace QuickTechSystems.WPF.ViewModels
{
    public class BulkProductViewModel : ViewModelBase
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private readonly IBarcodeService _barcodeService;
        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private bool? _dialogResult;
        private string _selectedQuickFillOption;
        private string _quickFillValue;
        private string _statusMessage;
        private bool _isSaving;
        private bool _selectAllForPrinting;
        private int _labelsPerProduct = 1;
        private int _selectedForPrintingCount;
        private Dictionary<int, List<string>> _validationErrors;

        public bool? DialogResult
        {
            get => _dialogResult;
            set => SetProperty(ref _dialogResult, value);
        }

        public ObservableCollection<ProductDTO> Products
        {
            get => _products;
            set => SetProperty(ref _products, value);
        }

        public ObservableCollection<CategoryDTO> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public ObservableCollection<SupplierDTO> Suppliers
        {
            get => _suppliers;
            set => SetProperty(ref _suppliers, value);
        }

        public string SelectedQuickFillOption
        {
            get => _selectedQuickFillOption;
            set => SetProperty(ref _selectedQuickFillOption, value);
        }

        public string QuickFillValue
        {
            get => _quickFillValue;
            set => SetProperty(ref _quickFillValue, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsSaving
        {
            get => _isSaving;
            set
            {
                SetProperty(ref _isSaving, value);
                OnPropertyChanged(nameof(IsNotSaving));
            }
        }

        public bool IsNotSaving => !IsSaving;

        public bool SelectAllForPrinting
        {
            get => _selectAllForPrinting;
            set
            {
                SetProperty(ref _selectAllForPrinting, value);
                UpdatePrintSelection(value);
            }
        }

        public int LabelsPerProduct
        {
            get => _labelsPerProduct;
            set => SetProperty(ref _labelsPerProduct, Math.Max(1, value));
        }

        public int SelectedForPrintingCount
        {
            get => _selectedForPrintingCount;
            set => SetProperty(ref _selectedForPrintingCount, value);
        }

        public Dictionary<int, List<string>> ValidationErrors
        {
            get => _validationErrors;
            set => SetProperty(ref _validationErrors, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand AddFiveRowsCommand { get; }
        public ICommand AddTenRowsCommand { get; }
        public ICommand ClearEmptyRowsCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand ApplyQuickFillCommand { get; }
        public ICommand GenerateBarcodeCommand { get; }
        public ICommand ImportFromExcelCommand { get; }
        public ICommand PrintBarcodesCommand { get; }

        public BulkProductViewModel(
            IProductService productService,
            ICategoryService categoryService,
            ISupplierService supplierService,
            IBarcodeService barcodeService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));
            _barcodeService = barcodeService ?? throw new ArgumentNullException(nameof(barcodeService));

            _products = new ObservableCollection<ProductDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();
            _validationErrors = new Dictionary<int, List<string>>();
            _selectedQuickFillOption = "Category";
            _quickFillValue = string.Empty;
            _statusMessage = string.Empty;

            SaveCommand = new AsyncRelayCommand(async _ => await SaveProductsAsync(), _ => !IsSaving);
            AddFiveRowsCommand = new RelayCommand(_ => AddEmptyRows(5), _ => !IsSaving);
            AddTenRowsCommand = new RelayCommand(_ => AddEmptyRows(10), _ => !IsSaving);
            ClearEmptyRowsCommand = new RelayCommand(_ => ClearEmptyRows(), _ => !IsSaving);
            ClearAllCommand = new RelayCommand(_ => ClearAllRows(), _ => !IsSaving);
            ApplyQuickFillCommand = new RelayCommand(ApplyQuickFill, _ => !IsSaving);
            GenerateBarcodeCommand = new RelayCommand(GenerateBarcode, _ => !IsSaving);
            ImportFromExcelCommand = new AsyncRelayCommand(async _ => await ImportFromExcel(), _ => !IsSaving);
            PrintBarcodesCommand = new AsyncRelayCommand(async _ => await PrintBarcodesAsync(), _ => !IsSaving);

            LoadInitialData();
            Products.Add(new ProductDTO { IsActive = true });

            // Subscribe to PropertyChanged events of Products
            Products.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (ProductDTO item in e.NewItems)
                    {
                        item.PropertyChanged += Product_PropertyChanged;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (ProductDTO item in e.OldItems)
                    {
                        item.PropertyChanged -= Product_PropertyChanged;
                    }
                }
            };
        }

        private void Product_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProductDTO.IsSelectedForPrinting))
            {
                UpdateSelectedForPrintingCount();
            }
        }

        private async void LoadInitialData()
        {
            try
            {
                var categories = await _categoryService.GetAllAsync();
                var suppliers = await _supplierService.GetAllAsync();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Categories = new ObservableCollection<CategoryDTO>(categories);
                    Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                });
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading data: {ex.Message}");
            }
        }

        private void UpdatePrintSelection(bool selectAll)
        {
            foreach (var product in Products)
            {
                product.IsSelectedForPrinting = selectAll;
            }
            UpdateSelectedForPrintingCount();
        }

        private void UpdateSelectedForPrintingCount()
        {
            SelectedForPrintingCount = Products.Count(p => p.IsSelectedForPrinting);
        }

        private async Task PrintBarcodesAsync()
        {
            try
            {
                var selectedProducts = Products.Where(p => p.IsSelectedForPrinting).ToList();

                if (!selectedProducts.Any())
                {
                    MessageBox.Show("Please select at least one product for printing.",
                        "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (LabelsPerProduct < 1)
                {
                    MessageBox.Show("Number of labels must be at least 1.",
                        "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusMessage = "Preparing barcodes for printing...";
                IsSaving = true;

                await Task.Run(async () =>
                {
                    foreach (var product in selectedProducts)
                    {
                        if (string.IsNullOrWhiteSpace(product.Barcode))
                        {
                            var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                            var random = new Random();
                            var randomDigits = random.Next(1000, 9999).ToString();
                            var categoryPrefix = product.CategoryId.ToString().PadLeft(3, '0');
                            product.Barcode = $"{categoryPrefix}{timestamp}{randomDigits}";
                        }

                        if (product.BarcodeImage == null)
                        {
                            product.BarcodeImage = _barcodeService.GenerateBarcode(product.Barcode);
                        }
                    }

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var printDialog = new PrintDialog();
                        if (printDialog.ShowDialog() == true)
                        {
                            var document = CreateBarcodeDocument(selectedProducts, LabelsPerProduct, printDialog);
                            printDialog.PrintDocument(document.DocumentPaginator, "Product Barcodes");
                        }
                    });
                });

                StatusMessage = "Barcodes printed successfully.";
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error printing barcodes: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
            }
        }

        private FixedDocument CreateBarcodeDocument(List<ProductDTO> products, int labelsPerProduct, PrintDialog printDialog)
        {
            var document = new FixedDocument();
            var pageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);
            var labelSize = new Size(96 * 2, 96); // 2 inches x 1 inch at 96 DPI
            var margin = new Thickness(96 * 0.5); // 0.5 inch margins

            var labelsPerRow = (int)((pageSize.Width - margin.Left - margin.Right) / labelSize.Width);
            var labelsPerColumn = (int)((pageSize.Height - margin.Top - margin.Bottom) / labelSize.Height);
            var labelsPerPage = labelsPerRow * labelsPerColumn;

            var currentPage = CreateNewPage(pageSize, margin);
            var currentPanel = (WrapPanel)((FixedPage)currentPage.Child).Children[0];
            var labelCount = 0;

            foreach (var product in products)
            {
                for (int i = 0; i < labelsPerProduct; i++)
                {
                    if (labelCount >= labelsPerPage)
                    {
                        document.Pages.Add(currentPage);
                        currentPage = CreateNewPage(pageSize, margin);
                        currentPanel = (WrapPanel)((FixedPage)currentPage.Child).Children[0];
                        labelCount = 0;
                    }

                    var label = CreateBarcodeLabel(product, labelSize);
                    currentPanel.Children.Add(label);
                    labelCount++;
                }
            }

            if (labelCount > 0)
            {
                document.Pages.Add(currentPage);
            }

            return document;
        }

        private PageContent CreateNewPage(Size pageSize, Thickness margin)
        {
            var page = new FixedPage
            {
                Width = pageSize.Width,
                Height = pageSize.Height
            };

            var panel = new WrapPanel
            {
                Margin = margin,
                Width = pageSize.Width - margin.Left - margin.Right
            };

            page.Children.Add(panel);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);

            return pageContent;
        }

        private UIElement CreateBarcodeLabel(ProductDTO product, Size labelSize)
        {
            var grid = new Grid
            {
                Width = labelSize.Width,
                Height = labelSize.Height,
                Margin = new Thickness(2)
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var image = new Image
            {
                Source = LoadBarcodeImage(product.BarcodeImage),
                Stretch = Stretch.Uniform
            };
            Grid.SetRow(image, 0);

            var barcodeText = new TextBlock
            {
                Text = product.Barcode,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(barcodeText, 1);

            var nameText = new TextBlock
            {
                Text = product.Name,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(nameText, 2);

            grid.Children.Add(image);
            grid.Children.Add(barcodeText);
            grid.Children.Add(nameText);

            return grid;
        }

        private BitmapImage LoadBarcodeImage(byte[]? imageData)
        {
            if (imageData == null) return null;

            var image = new BitmapImage();
            using (var ms = new MemoryStream(imageData))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
            }
            return image;
        }

        private async Task SaveProductsAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Validating products...";

                // Perform validation
                var validationResults = ValidateAllProducts();
                if (validationResults.Any())
                {
                    DisplayValidationErrors(validationResults);
                    return;
                }

                // Setup progress reporting
                var progress = new Progress<string>(status =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = status;
                    });
                });

                // Filter out empty products
                var productsToSave = Products.Where(p => !IsEmptyProduct(p)).ToList();

                if (!productsToSave.Any())
                {
                    MessageBox.Show("No valid products to save.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Pre-process all products before saving
                StatusMessage = "Preparing products for save...";
                foreach (var product in productsToSave)
                {
                    NormalizeProductData(product);
                    ValidateBarcode(product);
                }

                // Check for duplicate barcodes within the batch
                var duplicateBarcodes = productsToSave
                    .GroupBy(p => p.Barcode)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicateBarcodes.Any())
                {
                    MessageBox.Show($"Found duplicate barcodes within the batch: {string.Join(", ", duplicateBarcodes)}",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check for existing barcodes in the database
                StatusMessage = "Checking for duplicate barcodes...";
                var existingBarcodes = new List<string>();
                foreach (var product in productsToSave)
                {
                    try
                    {
                        var existingProduct = await _productService.FindProductByBarcodeAsync(product.Barcode);
                        if (existingProduct != null)
                        {
                            existingBarcodes.Add($"{product.Barcode} (used by {existingProduct.Name})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error checking barcode {product.Barcode}: {ex.Message}");
                        // Continue checking other barcodes
                    }
                }

                if (existingBarcodes.Any())
                {
                    MessageBox.Show($"Found barcodes that already exist in the database:\n{string.Join("\n", existingBarcodes)}",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusMessage = "Saving products...";
                var successCount = 0;
                var errors = new List<string>();

                try
                {
                    // Try batch save first
                    var savedProducts = await _productService.CreateBatchAsync(productsToSave, progress);
                    successCount = savedProducts.Count;
                }
                catch (Exception ex)
                {
                    // Get detailed error information
                    var error = GetDetailedErrorMessage(ex);
                    errors.Add($"Batch save failed: {error}");

                    // Ask if user wants to try individual saves as fallback
                    if (MessageBox.Show(
                            "Batch save failed. Would you like to try saving products individually?\n\n" +
                            "This may succeed for some products but not all.",
                            "Error", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        // Try individual save as fallback
                        for (int i = 0; i < productsToSave.Count; i++)
                        {
                            try
                            {
                                var product = productsToSave[i];
                                ((IProgress<string>)progress).Report($"Processing product {i + 1} of {productsToSave.Count}: {product.Name}");

                                // Double-check the product barcode isn't already used before trying to save it
                                var existingProduct = await _productService.FindProductByBarcodeAsync(product.Barcode);
                                if (existingProduct != null)
                                {
                                    errors.Add($"Skipped '{product.Name}': Barcode '{product.Barcode}' is already used by '{existingProduct.Name}'");
                                    continue;
                                }

                                await _productService.CreateAsync(product);
                                successCount++;
                            }
                            catch (Exception innerEx)
                            {
                                var detailedMsg = GetDetailedErrorMessage(innerEx, productsToSave[i].Name);
                                errors.Add(detailedMsg);
                            }
                        }
                    }
                }

                if (successCount > 0)
                {
                    MessageBox.Show($"Successfully saved {successCount} products.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    if (errors.Any())
                    {
                        var errorMessage = $"Some products failed to save:\n\n{string.Join("\n", errors)}";
                        MessageBox.Show(errorMessage, "Partial Success", MessageBoxButton.OK, MessageBoxImage.Warning);

                        // Log detailed errors
                        Debug.WriteLine("Bulk save partial errors:\n" + errorMessage);
                    }

                    DialogResult = true;
                }
                else if (errors.Any())
                {
                    var errorMessage = $"Failed to save any products:\n\n{string.Join("\n", errors)}";
                    MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    // Log detailed errors
                    Debug.WriteLine("Bulk save failed:\n" + errorMessage);
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error during save operation: {GetDetailedErrorMessage(ex)}";
                await ShowErrorMessageAsync(errorMessage);
                Debug.WriteLine("Bulk save unexpected error:\n" + errorMessage);
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
            }
        }

        private string GetDetailedErrorMessage(Exception ex, string context = "")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(context))
                sb.Append($"Error saving {context}: ");

            sb.Append(ex.Message);

            // Append inner exceptions for more detailed error information
            var currentEx = ex;
            while (currentEx.InnerException != null)
            {
                currentEx = currentEx.InnerException;
                sb.Append($"\n→ {currentEx.Message}");
            }

            // Add Entity Framework validation errors if available
            if (ex is DbUpdateException dbEx && dbEx.Entries != null && dbEx.Entries.Any())
            {
                sb.Append("\nEntity validation errors:");
                foreach (var entry in dbEx.Entries)
                {
                    var entity = entry.Entity;
                    sb.Append($"\n- {entity.GetType().Name}");
                }
            }

            return sb.ToString();
        }

        // Enhanced validation method to check for more issues
        private Dictionary<int, List<string>> ValidateAllProducts()
        {
            var errors = new Dictionary<int, List<string>>();

            for (int i = 0; i < Products.Count; i++)
            {
                var product = Products[i];
                var productErrors = new List<string>();

                if (IsEmptyProduct(product)) continue;

                // Basic validation
                if (string.IsNullOrWhiteSpace(product.Name))
                    productErrors.Add("Name is required");

                if (product.CategoryId <= 0)
                    productErrors.Add("Category is required");

                if (product.SalePrice <= 0)
                    productErrors.Add("Sale price must be greater than zero");

                if (product.PurchasePrice <= 0)
                    productErrors.Add("Purchase price must be greater than zero");

                if (product.CurrentStock < 0)
                    productErrors.Add("Current stock cannot be negative");

                if (product.MinimumStock < 0)
                    productErrors.Add("Minimum stock cannot be negative");

                // Additional validation
                if (product.SalePrice < product.PurchasePrice)
                    productErrors.Add("Sale price should not be less than purchase price");

                // Validate Speed format if provided
                if (!string.IsNullOrWhiteSpace(product.Speed))
                {
                    if (!decimal.TryParse(product.Speed, out _))
                    {
                        productErrors.Add("Speed must be a valid number");
                    }
                }

                // Barcode format validation if not empty
                if (!string.IsNullOrWhiteSpace(product.Barcode))
                {
                    if (product.Barcode.Length < 4)
                        productErrors.Add("Barcode must be at least 4 characters long");

                    if (!product.Barcode.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                        productErrors.Add("Barcode can only contain letters, numbers, hyphens, and underscores");
                }

                // Check for duplicate barcodes within the entries
                if (!string.IsNullOrWhiteSpace(product.Barcode))
                {
                    var duplicateIndices = new List<int>();

                    for (int j = 0; j < Products.Count; j++)
                    {
                        if (j != i && !IsEmptyProduct(Products[j]) &&
                            !string.IsNullOrWhiteSpace(Products[j].Barcode) &&
                            Products[j].Barcode.Equals(product.Barcode, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicateIndices.Add(j + 1); // +1 for human-readable row number
                        }
                    }

                    if (duplicateIndices.Any())
                        productErrors.Add($"Barcode '{product.Barcode}' is duplicated in rows: {string.Join(", ", duplicateIndices)}");
                }

                if (productErrors.Any())
                    errors.Add(i, productErrors);
            }

            return errors;
        }

        private void DisplayValidationErrors(Dictionary<int, List<string>> errors)
        {
            var errorMessage = new System.Text.StringBuilder("Please correct the following issues:\n\n");

            foreach (var error in errors)
            {
                errorMessage.AppendLine($"Row {error.Key + 1}:");
                foreach (var message in error.Value)
                {
                    errorMessage.AppendLine($"- {message}");
                }
                errorMessage.AppendLine();
            }

            MessageBox.Show(errorMessage.ToString(), "Validation Errors",
                MessageBoxButton.OK, MessageBoxImage.Warning);

            ValidationErrors = errors;
        }

        // Enhanced barcode generation and validation
        private void ValidateBarcode(ProductDTO product)
        {
            if (string.IsNullOrWhiteSpace(product.Barcode))
            {
                GenerateBarcodeForProduct(product);
                return;
            }

            // Clean barcode (only keep alphanumeric characters, hyphens, and underscores)
            product.Barcode = new string(product.Barcode.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());

            if (string.IsNullOrWhiteSpace(product.Barcode) || product.Barcode.Length < 4)
            {
                GenerateBarcodeForProduct(product);
            }
            else if (product.BarcodeImage == null)
            {
                // Generate barcode image if barcode exists but image doesn't
                product.BarcodeImage = _barcodeService.GenerateBarcode(product.Barcode);
            }
        }

        private void GenerateBarcodeForProduct(ProductDTO product)
        {
            var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8); // Use ticks for uniqueness
            var random = new Random();
            var randomDigits = random.Next(1000, 9999).ToString();
            var categoryPrefix = (product.CategoryId > 0 ? product.CategoryId.ToString() : "000").PadLeft(3, '0');

            product.Barcode = $"{categoryPrefix}-{timestamp}-{randomDigits}";
            product.BarcodeImage = _barcodeService.GenerateBarcode(product.Barcode);
        }

        // Enhanced NormalizeProductData method
        private void NormalizeProductData(ProductDTO product)
        {
            // Trim text fields
            product.Name = product.Name?.Trim() ?? string.Empty;
            product.Barcode = product.Barcode?.Trim() ?? string.Empty;
            product.Description = product.Description?.Trim();
            product.Speed = product.Speed?.Trim();

            // Ensure non-negative values
            product.CurrentStock = Math.Max(0, product.CurrentStock);
            product.MinimumStock = Math.Max(0, product.MinimumStock);
            product.SalePrice = Math.Max(0, product.SalePrice);
            product.PurchasePrice = Math.Max(0, product.PurchasePrice);

            // Ensure price relationships are sensible
            if (product.SalePrice < product.PurchasePrice)
                product.SalePrice = product.PurchasePrice;

            // Set creation time if not already set
            if (product.CreatedAt == default)
                product.CreatedAt = DateTime.Now;

            // Ensure UpdatedAt is set
            product.UpdatedAt = DateTime.Now;

            // Ensure IsActive is set
            product.IsActive = true;
        }

        private bool IsEmptyProduct(ProductDTO product)
        {
            return string.IsNullOrWhiteSpace(product.Name) &&
                   string.IsNullOrWhiteSpace(product.Barcode) &&
                   product.CategoryId <= 0 &&
                   !product.SupplierId.HasValue &&
                   product.PurchasePrice == 0 &&
                   product.SalePrice == 0 &&
                   product.CurrentStock == 0 &&
                   product.MinimumStock == 0 &&
                   string.IsNullOrWhiteSpace(product.Speed);
        }

        private void AddEmptyRows(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Products.Add(new ProductDTO { IsActive = true });
            }
            OnPropertyChanged(nameof(Products));
        }

        private void ClearEmptyRows()
        {
            var emptyRows = Products.Where(IsEmptyProduct).ToList();
            foreach (var row in emptyRows)
            {
                Products.Remove(row);
            }

            if (Products.Count == 0)
            {
                Products.Add(new ProductDTO { IsActive = true });
            }

            ValidationErrors.Clear();
            OnPropertyChanged(nameof(Products));
        }

        private void ClearAllRows()
        {
            if (MessageBox.Show("Are you sure you want to clear all rows?",
                "Confirm Clear All", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                Products.Clear();
                Products.Add(new ProductDTO { IsActive = true });
                ValidationErrors.Clear();
                OnPropertyChanged(nameof(Products));
            }
        }

        private void ApplyQuickFill(object? parameter)
        {
            var selectedProducts = Products.Where(p => p.IsSelected).ToList();
            if (!selectedProducts.Any())
            {
                MessageBox.Show("Please select at least one row to apply quick fill.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                switch (SelectedQuickFillOption)
                {
                    case "Category":
                        if (int.TryParse(QuickFillValue, out int categoryId))
                        {
                            var category = Categories.FirstOrDefault(c => c.CategoryId == categoryId);
                            if (category != null)
                            {
                                foreach (var product in selectedProducts)
                                {
                                    product.CategoryId = category.CategoryId;
                                    product.CategoryName = category.Name;
                                }
                            }
                        }
                        break;

                    case "Supplier":
                        if (int.TryParse(QuickFillValue, out int supplierId))
                        {
                            var supplier = Suppliers.FirstOrDefault(s => s.SupplierId == supplierId);
                            if (supplier != null)
                            {
                                foreach (var product in selectedProducts)
                                {
                                    product.SupplierId = supplier.SupplierId;
                                    product.SupplierName = supplier.Name;
                                }
                            }
                        }
                        break;

                    case "Purchase Price":
                        if (decimal.TryParse(QuickFillValue, out decimal purchasePrice))
                        {
                            foreach (var product in selectedProducts)
                            {
                                product.PurchasePrice = Math.Max(0, purchasePrice);
                                if (product.SalePrice < purchasePrice)
                                {
                                    product.SalePrice = purchasePrice;
                                }
                            }
                        }
                        break;

                    case "Sale Price":
                        if (decimal.TryParse(QuickFillValue, out decimal salePrice))
                        {
                            foreach (var product in selectedProducts)
                            {
                                product.SalePrice = Math.Max(product.PurchasePrice, salePrice);
                            }
                        }
                        break;

                    case "Stock Values":
                        if (int.TryParse(QuickFillValue, out int stockValue))
                        {
                            foreach (var product in selectedProducts)
                            {
                                product.CurrentStock = Math.Max(0, stockValue);
                                product.MinimumStock = Math.Max(0, stockValue / 2);
                            }
                        }
                        break;

                    case "Speed":
                        if (decimal.TryParse(QuickFillValue, out decimal speedValue))
                        {
                            foreach (var product in selectedProducts)
                            {
                                product.Speed = speedValue.ToString();
                            }
                        }
                        else
                        {
                            MessageBox.Show("Please enter a valid number for speed.",
                                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        break;
                }

                OnPropertyChanged(nameof(Products));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying quick fill: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateBarcode(object? parameter)
        {
            try
            {
                if (parameter is not ProductDTO product) return;

                var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                var random = new Random();
                var randomDigits = random.Next(1000, 9999).ToString();
                var categoryPrefix = product.CategoryId.ToString().PadLeft(3, '0');

                product.Barcode = $"{categoryPrefix}-{timestamp}-{randomDigits}";
                product.BarcodeImage = _barcodeService.GenerateBarcode(product.Barcode);

                OnPropertyChanged(nameof(Products));

                MessageBox.Show($"Barcode generated: {product.Barcode}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {

                Debug.WriteLine($"Error generating barcode: {ex.Message}");
                MessageBox.Show($"Error generating barcode: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ImportFromExcel()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Excel Files|*.xlsx;*.xls|All files (*.*)|*.*",
                    Title = "Select Excel File"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    StatusMessage = "Excel import feature is planned for a future update.";
                    await Task.Delay(2000);
                    StatusMessage = string.Empty;
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error importing from Excel: {ex.Message}");
            }
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }
}