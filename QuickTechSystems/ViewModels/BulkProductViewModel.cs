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
using System.Threading;
using System.Data;
using System.Globalization;
using OfficeOpenXml; // EPPlus namespace

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
        private CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;

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
        public ICommand SelectAllCommand { get; }
        public ICommand AddOneRowCommand { get; }
        public ICommand CancelOperationCommand { get; }

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
            _cancellationTokenSource = new CancellationTokenSource();
            AddOneRowCommand = new RelayCommand(_ => AddEmptyRows(1), _ => !IsSaving);  // New command for adding a single row
            SaveCommand = new AsyncRelayCommand(async _ => await SaveProductsAsync(), _ => !IsSaving);
            AddFiveRowsCommand = new RelayCommand(_ => AddEmptyRows(5), _ => !IsSaving);
            AddTenRowsCommand = new RelayCommand(_ => AddEmptyRows(10), _ => !IsSaving);
            ClearEmptyRowsCommand = new RelayCommand(_ => ClearEmptyRows(), _ => !IsSaving);
            ClearAllCommand = new RelayCommand(_ => ClearAllRows(), _ => !IsSaving);
            ApplyQuickFillCommand = new RelayCommand(ApplyQuickFill, _ => !IsSaving);
            GenerateBarcodeCommand = new RelayCommand(GenerateBarcode, _ => !IsSaving);
            ImportFromExcelCommand = new AsyncRelayCommand(async _ => await ImportFromExcelAsync(), _ => !IsSaving);
            PrintBarcodesCommand = new AsyncRelayCommand(async _ => await PrintBarcodesAsync(), _ => !IsSaving);
            SelectAllCommand = new RelayCommand(_ => SelectAllProducts());
            CancelOperationCommand = new RelayCommand(_ => CancelCurrentOperation(), _ => IsSaving);

            LoadInitialData();
            Products.Add(CreateEmptyProduct());

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

        private ProductDTO CreateEmptyProduct()
        {
            return new ProductDTO
            {
                IsActive = true,
                CreatedAt = DateTime.Now,
                MinimumStock = 0,
                CurrentStock = 0
            };
        }

        private void Product_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProductDTO.IsSelectedForPrinting))
            {
                UpdateSelectedForPrintingCount();
            }
            else if (e.PropertyName == nameof(ProductDTO.CategoryId))
            {
                UpdateCategoryName(sender as ProductDTO);
            }
            else if (e.PropertyName == nameof(ProductDTO.SupplierId))
            {
                UpdateSupplierName(sender as ProductDTO);
            }
        }

        private void UpdateCategoryName(ProductDTO product)
        {
            if (product == null || product.CategoryId <= 0) return;

            var category = Categories.FirstOrDefault(c => c.CategoryId == product.CategoryId);
            if (category != null)
            {
                product.CategoryName = category.Name;
            }
        }

        private void UpdateSupplierName(ProductDTO product)
        {
            if (product == null || !product.SupplierId.HasValue || product.SupplierId <= 0) return;

            var supplier = Suppliers.FirstOrDefault(s => s.SupplierId == product.SupplierId);
            if (supplier != null)
            {
                product.SupplierName = supplier.Name;
            }
        }

        private async void LoadInitialData()
        {
            try
            {
                var categories = await _categoryService.GetActiveAsync();
                var suppliers = await _supplierService.GetActiveAsync();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Categories = new ObservableCollection<CategoryDTO>(categories);
                    Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                    Debug.WriteLine($"Loaded {categories.Count()} categories and {suppliers.Count()} suppliers");
                });
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading data: {ex.Message}");
                Debug.WriteLine($"Error loading initial data: {ex}");
            }
        }

        private void SelectAllProducts()
        {
            foreach (var product in Products)
            {
                product.IsSelected = true;
            }
        }

        private void CancelCurrentOperation()
        {
            try
            {
                if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource = new CancellationTokenSource();
                    StatusMessage = "Operation cancelled.";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cancelling operation: {ex.Message}");
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
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryMessage("Another operation is in progress. Please wait.", MessageBoxImage.Warning);
                return;
            }

            try
            {
                var selectedProducts = Products.Where(p => p.IsSelectedForPrinting).ToList();

                if (!selectedProducts.Any())
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Please select at least one product for printing.",
                            "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    return;
                }

                if (LabelsPerProduct < 1)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Number of labels must be at least 1.",
                            "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    return;
                }

                StatusMessage = "Preparing barcodes for printing...";
                IsSaving = true;

                // Generate missing barcodes first
                int generatedCount = 0;
                foreach (var product in selectedProducts)
                {
                    if (string.IsNullOrWhiteSpace(product.Barcode))
                    {
                        var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                        var random = new Random();
                        var randomDigits = random.Next(1000, 9999).ToString();
                        var categoryPrefix = product.CategoryId.ToString().PadLeft(3, '0');
                        product.Barcode = $"{categoryPrefix}-{timestamp}-{randomDigits}";
                        generatedCount++;
                        StatusMessage = $"Generated {generatedCount} barcodes...";
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

                StatusMessage = "Barcodes printed successfully.";
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error printing barcodes: {ex.Message}");
                Debug.WriteLine($"Error printing barcodes: {ex}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }

        private FixedDocument CreateBarcodeDocument(List<ProductDTO> products, int labelsPerProduct, PrintDialog printDialog)
        {
            var document = new FixedDocument();
            var pageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);
            var labelSize = new Size(96 * 2.5, 96 * 1.2); // Increased from 2x1 to 2.5x1.2 inches for better fit
            var margin = new Thickness(96 * 0.25); // 0.25 inch margins for efficient space usage

            // Calculate how many labels can fit on the page
            var labelsPerRow = Math.Max(1, (int)((pageSize.Width - margin.Left - margin.Right) / labelSize.Width));
            var labelsPerColumn = Math.Max(1, (int)((pageSize.Height - margin.Top - margin.Bottom) / labelSize.Height));
            var labelsPerPage = labelsPerRow * labelsPerColumn;

            Debug.WriteLine($"Page can fit {labelsPerRow}x{labelsPerColumn} = {labelsPerPage} labels");

            var currentPage = CreateNewPage(pageSize, margin);
            var currentPanel = (WrapPanel)((FixedPage)currentPage.Child).Children[0];

            // Configure WrapPanel for optimal layout
            currentPanel.ItemWidth = labelSize.Width;
            currentPanel.ItemHeight = labelSize.Height;

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
                        currentPanel.ItemWidth = labelSize.Width;
                        currentPanel.ItemHeight = labelSize.Height;
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
                Width = pageSize.Width - margin.Left - margin.Right,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };

            page.Children.Add(panel);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);

            return pageContent;
        }
        private UIElement CreateBarcodeLabel(ProductDTO product, Size labelSize)
        {
            // Create a border for visual separation and padding
            var border = new Border
            {
                Width = labelSize.Width - 8, // Slightly smaller than container for separation
                Height = labelSize.Height - 8,
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Colors.White)
            };

            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Improved row definitions with better proportions
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2.5, GridUnitType.Star) }); // Increased height for barcode
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Ensure the barcode image exists
            if (product.BarcodeImage == null && !string.IsNullOrWhiteSpace(product.Barcode))
            {
                try
                {
                    product.BarcodeImage = _barcodeService.GenerateBarcode(product.Barcode);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                }
            }

            // Create barcode image with explicit alignment
            var image = new Image
            {
                Source = LoadBarcodeImage(product.BarcodeImage),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxHeight = (labelSize.Height - 8) * 0.65, // Constrain height to 65% of label height
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(image, 0);

            // Improved barcode text
            var barcodeText = new TextBlock
            {
                Text = product.Barcode,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 10,
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetRow(barcodeText, 1);

            // Improved product name text
            var nameText = new TextBlock
            {
                Text = product.Name,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                MaxWidth = labelSize.Width - 24, // Allow for padding
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(nameText, 2);

            grid.Children.Add(image);
            grid.Children.Add(barcodeText);
            grid.Children.Add(nameText);

            border.Child = grid;

            return border;
        }
        private BitmapImage LoadBarcodeImage(byte[]? imageData)
        {
            if (imageData == null) return null;

            var image = new BitmapImage();
            try
            {
                using (var ms = new MemoryStream(imageData))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                    image.Freeze(); // Important for cross-thread usage
                }
                return image;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading barcode image: {ex.Message}");
                return null;
            }
        }

        private async Task SaveProductsAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryMessage("Another operation is already in progress. Please wait.", MessageBoxImage.Warning);
                return;
            }

            try
            {
                IsSaving = true;
                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;

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
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("No valid products to save.", "Information",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    return;
                }

                // Pre-process all products before saving
                StatusMessage = "Preparing products for save...";
                foreach (var product in productsToSave)
                {
                    token.ThrowIfCancellationRequested();
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
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Found duplicate barcodes within the batch: {string.Join(", ", duplicateBarcodes)}",
                            "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    return;
                }

                // Check for existing barcodes in the database
                StatusMessage = "Checking for duplicate barcodes...";
                var existingBarcodes = new List<string>();
                foreach (var product in productsToSave)
                {
                    token.ThrowIfCancellationRequested();

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
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Found barcodes that already exist in the database:\n{string.Join("\n", existingBarcodes)}",
                            "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
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
                catch (OperationCanceledException)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Operation was cancelled by the user.", "Cancelled",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    return;
                }
                catch (Exception ex)
                {
                    // Get detailed error information
                    var error = GetDetailedErrorMessage(ex);
                    errors.Add($"Batch save failed: {error}");
                    Debug.WriteLine($"Batch save error: {error}");

                    // Ask if user wants to try individual saves as fallback
                    var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        return MessageBox.Show(
                            "Batch save failed. Would you like to try saving products individually?\n\n" +
                            "This may succeed for some products but not all.",
                            "Error", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    });

                    if (result == MessageBoxResult.Yes)
                    {
                        // Try individual save as fallback
                        for (int i = 0; i < productsToSave.Count; i++)
                        {
                            try
                            {
                                token.ThrowIfCancellationRequested();

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
                            catch (OperationCanceledException)
                            {
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    MessageBox.Show("Operation was cancelled by the user.", "Cancelled",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                });
                                return;
                            }
                            catch (Exception innerEx)
                            {
                                var detailedMsg = GetDetailedErrorMessage(innerEx, productsToSave[i].Name);
                                errors.Add(detailedMsg);
                                Debug.WriteLine($"Individual save error: {detailedMsg}");
                            }
                        }
                    }
                }

                if (successCount > 0)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Successfully saved {successCount} products.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    });

                    if (errors.Any())
                    {
                        var errorMessage = $"Some products failed to save:\n\n{string.Join("\n", errors)}";
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(errorMessage, "Partial Success", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });

                        // Log detailed errors
                        Debug.WriteLine("Bulk save partial errors:\n" + string.Join("\n", errors));
                    }

                    DialogResult = true;
                }
                else if (errors.Any())
                {
                    var errorMessage = $"Failed to save any products:\n\n{string.Join("\n", errors)}";
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });

                    // Log detailed errors
                    Debug.WriteLine("Bulk save complete failure:\n" + string.Join("\n", errors));
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled.";
                await Task.Delay(2000);
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
                _operationLock.Release();
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

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(errorMessage.ToString(), "Validation Errors",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            });

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
            try
            {
                product.BarcodeImage = _barcodeService.GenerateBarcode(product.Barcode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating barcode image: {ex.Message}");
            }
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

            // Update category and supplier names
            UpdateCategoryName(product);
            UpdateSupplierName(product);
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
                Products.Add(CreateEmptyProduct());
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
                Products.Add(CreateEmptyProduct());
            }

            ValidationErrors.Clear();
            OnPropertyChanged(nameof(Products));
        }

        private void ClearAllRows()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (MessageBox.Show("Are you sure you want to clear all rows?",
                    "Confirm Clear All", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    Products.Clear();
                    Products.Add(CreateEmptyProduct());
                    ValidationErrors.Clear();
                    OnPropertyChanged(nameof(Products));
                }
            });
        }

        private void ApplyQuickFill(object? parameter)
        {
            var selectedProducts = Products.Where(p => p.IsSelected).ToList();
            if (!selectedProducts.Any())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Please select at least one row to apply quick fill.",
                        "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
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
                            else
                            {
                                ShowTemporaryMessage($"Category ID {categoryId} not found.", MessageBoxImage.Warning);
                            }
                        }
                        else
                        {
                            // Try to find by name
                            var category = Categories.FirstOrDefault(c =>
                                c.Name.Equals(QuickFillValue, StringComparison.OrdinalIgnoreCase));

                            if (category != null)
                            {
                                foreach (var product in selectedProducts)
                                {
                                    product.CategoryId = category.CategoryId;
                                    product.CategoryName = category.Name;
                                }
                            }
                            else
                            {
                                ShowTemporaryMessage("Please enter a valid category ID or name.", MessageBoxImage.Warning);
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
                            else
                            {
                                ShowTemporaryMessage($"Supplier ID {supplierId} not found.", MessageBoxImage.Warning);
                            }
                        }
                        else
                        {
                            // Try to find by name
                            var supplier = Suppliers.FirstOrDefault(s =>
                                s.Name.Equals(QuickFillValue, StringComparison.OrdinalIgnoreCase));

                            if (supplier != null)
                            {
                                foreach (var product in selectedProducts)
                                {
                                    product.SupplierId = supplier.SupplierId;
                                    product.SupplierName = supplier.Name;
                                }
                            }
                            else
                            {
                                ShowTemporaryMessage("Please enter a valid supplier ID or name.", MessageBoxImage.Warning);
                            }
                        }
                        break;

                    case "Purchase Price":
                        if (decimal.TryParse(QuickFillValue, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal purchasePrice))
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
                        else
                        {
                            ShowTemporaryMessage("Please enter a valid number for purchase price.", MessageBoxImage.Warning);
                        }
                        break;

                    case "Sale Price":
                        if (decimal.TryParse(QuickFillValue, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal salePrice))
                        {
                            foreach (var product in selectedProducts)
                            {
                                product.SalePrice = Math.Max(product.PurchasePrice, salePrice);
                            }
                        }
                        else
                        {
                            ShowTemporaryMessage("Please enter a valid number for sale price.", MessageBoxImage.Warning);
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
                        else
                        {
                            ShowTemporaryMessage("Please enter a valid number for stock value.", MessageBoxImage.Warning);
                        }
                        break;

                    case "Speed":
                        if (decimal.TryParse(QuickFillValue, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal speedValue))
                        {
                            foreach (var product in selectedProducts)
                            {
                                product.Speed = speedValue.ToString(CultureInfo.InvariantCulture);
                            }
                        }
                        else
                        {
                            ShowTemporaryMessage("Please enter a valid number for speed.", MessageBoxImage.Warning);
                        }
                        break;
                }

                OnPropertyChanged(nameof(Products));
            }
            catch (Exception ex)
            {
                ShowTemporaryMessage($"Error applying quick fill: {ex.Message}", MessageBoxImage.Error);
                Debug.WriteLine($"Error applying quick fill: {ex}");
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
                var categoryPrefix = (product.CategoryId > 0 ? product.CategoryId.ToString() : "000").PadLeft(3, '0');

                product.Barcode = $"{categoryPrefix}-{timestamp}-{randomDigits}";
                product.BarcodeImage = _barcodeService.GenerateBarcode(product.Barcode);

                OnPropertyChanged(nameof(Products));

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Barcode generated: {product.Barcode}", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                ShowTemporaryMessage($"Error generating barcode: {ex.Message}", MessageBoxImage.Error);
                Debug.WriteLine($"Error generating barcode: {ex}");
            }
        }

        private async Task ImportFromExcelAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryMessage("Another operation is in progress. Please wait.", MessageBoxImage.Warning);
                return;
            }

            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Excel Files|*.xlsx;*.xls|CSV Files|*.csv|All files (*.*)|*.*",
                    Title = "Select Excel or CSV File"
                };

                var result = openFileDialog.ShowDialog();
                if (result == true)
                {
                    string filePath = openFileDialog.FileName;
                    string fileExtension = Path.GetExtension(filePath).ToLower();

                    StatusMessage = "Reading file...";
                    IsSaving = true;
                    _cancellationTokenSource = new CancellationTokenSource();
                    var token = _cancellationTokenSource.Token;

                    // Process based on file type
                    if (fileExtension == ".csv")
                    {
                        await ImportFromCsvFile(filePath, token);
                    }
                    else if (fileExtension == ".xlsx" || fileExtension == ".xls")
                    {
                        await ImportFromExcelFileUsingEPPlus(filePath, token);
                    }
                    else
                    {
                        MessageBox.Show("Unsupported file format. Please select an Excel or CSV file.",
                            "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Import cancelled.";
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error importing file: {ex.Message}");
                Debug.WriteLine($"Error in file import: {ex}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }

        private async Task ImportFromCsvFile(string filePath, CancellationToken token)
        {
            var importedProducts = new List<ProductDTO>();
            var errors = new List<string>();

            await Task.Run(() =>
            {
                try
                {
                    // Read all lines from the CSV file
                    var lines = File.ReadAllLines(filePath);
                    if (lines.Length <= 1)
                    {
                        throw new InvalidOperationException("CSV file contains no data rows.");
                    }

                    // Process header row to identify columns
                    var headerLine = lines[0];
                    var headers = SplitCsvLine(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToArray();
                    var columnMappings = new Dictionary<string, int>();

                    for (int i = 0; i < headers.Length; i++)
                    {
                        columnMappings[headers[i]] = i;
                    }

                    // Required columns check
                    var requiredColumns = new[] { "name", "category", "purchase price", "sale price" };
                    var missingColumns = requiredColumns.Where(col => !columnMappings.Keys.Any(key => key.Contains(col))).ToList();

                    if (missingColumns.Any())
                    {
                        throw new InvalidOperationException($"Missing required columns: {string.Join(", ", missingColumns)}");
                    }

                    // Process data rows
                    for (int rowIndex = 1; rowIndex < lines.Length; rowIndex++)
                    {
                        token.ThrowIfCancellationRequested();

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Processing row {rowIndex} of {lines.Length - 1}...";
                        });

                        try
                        {
                            var line = lines[rowIndex];
                            var values = SplitCsvLine(line);

                            if (values.Length < headers.Length)
                            {
                                errors.Add($"Row {rowIndex + 1}: Not enough columns");
                                continue;
                            }

                            var product = CreateEmptyProduct();

                            // Process each column based on header mapping
                            foreach (var mapping in columnMappings)
                            {
                                if (mapping.Value >= values.Length) continue;

                                var value = values[mapping.Value]?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(value)) continue;

                                ProcessProductValue(product, mapping.Key, value, rowIndex + 1, errors);
                            }

                            // Validate product
                            if (IsValidImportedProduct(product, rowIndex + 1, errors))
                            {
                                importedProducts.Add(product);
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Row {rowIndex + 1}: {ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error processing CSV file: {ex.Message}",
                            "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Debug.WriteLine($"CSV import error: {ex}");
                    });
                    return;
                }

                UpdateUIWithImportedProducts(importedProducts, errors);
            });
        }

        private async Task ImportFromExcelFileUsingEPPlus(string filePath, CancellationToken token)
        {
            var importedProducts = new List<ProductDTO>();
            var errors = new List<string>();

            await Task.Run(() =>
            {
                try
                {
                    // Set the license context for EPPlus - this line fixes the ambiguous reference error
                    ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                    using (var package = new ExcelPackage(new FileInfo(filePath)))
                    {
                        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                        if (worksheet == null)
                        {
                            throw new InvalidOperationException("No worksheet found in the Excel file.");
                        }

                        // Get row and column counts
                        int rowCount = worksheet.Dimension?.Rows ?? 0;
                        int colCount = worksheet.Dimension?.Columns ?? 0;

                        if (rowCount <= 1) // Header row only or empty
                        {
                            throw new InvalidOperationException("Excel file contains no data rows.");
                        }

                        // Process header row to identify columns
                        var columnMappings = new Dictionary<string, int>();
                        for (int col = 1; col <= colCount; col++)
                        {
                            var headerValue = worksheet.Cells[1, col].Text?.Trim();
                            if (!string.IsNullOrEmpty(headerValue))
                            {
                                columnMappings[headerValue.ToLowerInvariant()] = col;
                            }
                        }

                        // Required columns check
                        var requiredColumns = new[] { "name", "category", "purchase price", "sale price" };
                        var missingColumns = requiredColumns.Where(col => !columnMappings.Keys.Any(key => key.Contains(col))).ToList();

                        if (missingColumns.Any())
                        {
                            throw new InvalidOperationException($"Missing required columns: {string.Join(", ", missingColumns)}");
                        }

                        // Process data rows
                        for (int row = 2; row <= rowCount; row++)
                        {
                            token.ThrowIfCancellationRequested();

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = $"Processing row {row - 1} of {rowCount - 1}...";
                            });

                            try
                            {
                                var product = CreateEmptyProduct();

                                // Extract product data from the row
                                foreach (var mapping in columnMappings)
                                {
                                    var value = worksheet.Cells[row, mapping.Value].Text?.Trim();
                                    if (string.IsNullOrEmpty(value)) continue;

                                    ProcessProductValue(product, mapping.Key, value, row, errors);
                                }

                                // Validate product
                                if (IsValidImportedProduct(product, row, errors))
                                {
                                    importedProducts.Add(product);
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Row {row}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error processing Excel file: {ex.Message}",
                            "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Debug.WriteLine($"Excel import error: {ex}");
                    });
                    return;
                }

                UpdateUIWithImportedProducts(importedProducts, errors);
            });
        }

        private void ProcessProductValue(ProductDTO product, string columnName, string value, int rowIndex, List<string> errors)
        {
            switch (columnName)
            {
                case var col when col.Contains("name"):
                    product.Name = value;
                    break;
                case var col when col.Contains("barcode"):
                    product.Barcode = value;
                    break;
                case var col when col.Contains("description"):
                    product.Description = value;
                    break;
                case var col when col.Contains("category"):
                    // Try to match by name first
                    var category = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        Categories.FirstOrDefault(c => c.Name.Equals(value, StringComparison.OrdinalIgnoreCase)));

                    if (category != null)
                    {
                        product.CategoryId = category.CategoryId;
                        product.CategoryName = category.Name;
                    }
                    else if (int.TryParse(value, out int categoryId))
                    {
                        // Try as ID if name match fails
                        category = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            Categories.FirstOrDefault(c => c.CategoryId == categoryId));

                        if (category != null)
                        {
                            product.CategoryId = category.CategoryId;
                            product.CategoryName = category.Name;
                        }
                        else
                        {
                            errors.Add($"Row {rowIndex}: Category '{value}' not found");
                        }
                    }
                    else
                    {
                        errors.Add($"Row {rowIndex}: Category '{value}' not found");
                    }
                    break;
                case var col when col.Contains("supplier"):
                    // Try to match by name first
                    var supplier = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        Suppliers.FirstOrDefault(s => s.Name.Equals(value, StringComparison.OrdinalIgnoreCase)));

                    if (supplier != null)
                    {
                        product.SupplierId = supplier.SupplierId;
                        product.SupplierName = supplier.Name;
                    }
                    else if (int.TryParse(value, out int supplierId))
                    {
                        // Try as ID if name match fails
                        supplier = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            Suppliers.FirstOrDefault(s => s.SupplierId == supplierId));

                        if (supplier != null)
                        {
                            product.SupplierId = supplier.SupplierId;
                            product.SupplierName = supplier.Name;
                        }
                    }
                    // Not adding an error since supplier is optional
                    break;
                case var col when col.Contains("purchase price"):
                    if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal purchasePrice))
                    {
                        product.PurchasePrice = Math.Max(0, purchasePrice);
                    }
                    else
                    {
                        errors.Add($"Row {rowIndex}: Invalid purchase price '{value}'");
                    }
                    break;
                case var col when col.Contains("sale price"):
                    if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal salePrice))
                    {
                        product.SalePrice = Math.Max(0, salePrice);
                    }
                    else
                    {
                        errors.Add($"Row {rowIndex}: Invalid sale price '{value}'");
                    }
                    break;
                case var col when col.Contains("current stock"):
                    if (int.TryParse(value, out int currentStock))
                    {
                        product.CurrentStock = Math.Max(0, currentStock);
                    }
                    else
                    {
                        errors.Add($"Row {rowIndex}: Invalid current stock '{value}'");
                    }
                    break;
                case var col when col.Contains("minimum stock"):
                    if (int.TryParse(value, out int minimumStock))
                    {
                        product.MinimumStock = Math.Max(0, minimumStock);
                    }
                    else
                    {
                        errors.Add($"Row {rowIndex}: Invalid minimum stock '{value}'");
                    }
                    break;
                case var col when col.Contains("speed"):
                    product.Speed = value;
                    break;
            }
        }

        private bool IsValidImportedProduct(ProductDTO product, int rowIndex, List<string> errors)
        {
            if (!string.IsNullOrWhiteSpace(product.Name) &&
                product.CategoryId > 0 &&
                product.PurchasePrice > 0 &&
                product.SalePrice > 0)
            {
                return true;
            }
            else if (!string.IsNullOrWhiteSpace(product.Name)) // If it has a name but is missing other required fields
            {
                var missingFields = new List<string>();
                if (product.CategoryId <= 0) missingFields.Add("Category");
                if (product.PurchasePrice <= 0) missingFields.Add("Purchase Price");
                if (product.SalePrice <= 0) missingFields.Add("Sale Price");

                errors.Add($"Row {rowIndex}: Product '{product.Name}' is missing required fields: {string.Join(", ", missingFields)}");
            }
            return false;
        }

        private string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            var currentValue = new StringBuilder();
            bool insideQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    // If we encounter a double quote inside quoted text, it might be an escaped quote
                    if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // This is an escaped quote - add a single quote to the value and skip the next char
                        currentValue.Append('"');
                        i++;
                    }
                    else
                    {
                        // Toggle the insideQuotes flag
                        insideQuotes = !insideQuotes;
                    }
                }
                else if (c == ',' && !insideQuotes)
                {
                    // End of field
                    result.Add(currentValue.ToString());
                    currentValue.Clear();
                }
                else
                {
                    // Regular character, add to the value
                    currentValue.Append(c);
                }
            }

            // Add the last field
            result.Add(currentValue.ToString());

            return result.ToArray();
        }

        private void UpdateUIWithImportedProducts(List<ProductDTO> importedProducts, List<string> errors)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (importedProducts.Any())
                {
                    // Ask user if they want to replace or append
                    var hasExistingProducts = Products.Any(p => !IsEmptyProduct(p));
                    bool replaceExisting = true;

                    if (hasExistingProducts)
                    {
                        var response = MessageBox.Show(
                            $"Found {importedProducts.Count} products in the file. Do you want to replace existing products?",
                            "Import Options",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        replaceExisting = response == MessageBoxResult.Yes;
                    }

                    if (replaceExisting)
                    {
                        Products.Clear();
                    }
                    else
                    {
                        ClearEmptyRows(); // Remove any empty rows before adding imported products
                    }

                    foreach (var product in importedProducts)
                    {
                        Products.Add(product);
                    }

                    // Ensure at least one empty row at the end
                    if (!Products.Any(IsEmptyProduct))
                    {
                        Products.Add(CreateEmptyProduct());
                    }

                    // Report success
                    MessageBox.Show(
                        $"Successfully imported {importedProducts.Count} products." +
                        (errors.Any() ? $"\n\nWarning: {errors.Count} rows had errors." : ""),
                        "Import Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // If there were errors, offer to show them
                    if (errors.Any())
                    {
                        var showErrors = MessageBox.Show(
                            "Would you like to see the detailed error report?",
                            "Import Warnings",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (showErrors == MessageBoxResult.Yes)
                        {
                            MessageBox.Show(
                                string.Join("\n", errors),
                                "Import Error Details",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                }
                else
                {
                    MessageBox.Show(
                        "No valid products were found in the file. Please check the file format and try again.",
                        "Import Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });
        }

        private void ShowTemporaryMessage(string message, MessageBoxImage icon = MessageBoxImage.Information)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message,
                    icon == MessageBoxImage.Error ? "Error" :
                    icon == MessageBoxImage.Warning ? "Warning" : "Information",
                    MessageBoxButton.OK, icon);
            });
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        public override void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                // Cancel any running operations
                try
                {
                    if (_cancellationTokenSource != null)
                    {
                        _cancellationTokenSource.Cancel();
                        _cancellationTokenSource.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during token source disposal: {ex.Message}");
                }

                // Dispose the operation lock
                try
                {
                    _operationLock?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during operation lock disposal: {ex.Message}");
                }

                // Unsubscribe from product property change events
                foreach (var product in Products)
                {
                    product.PropertyChanged -= Product_PropertyChanged;
                }

                base.Dispose();
            }
        }
    }
}