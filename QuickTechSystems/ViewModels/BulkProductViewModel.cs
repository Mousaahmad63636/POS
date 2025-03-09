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
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Windows.Markup;

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

                var validationResults = ValidateAllProducts();
                if (validationResults.Any())
                {
                    DisplayValidationErrors(validationResults);
                    return;
                }

                var progress = new Progress<string>(status =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = status;
                    });
                });

                var successCount = 0;
                var errors = new List<string>();

                await Task.Run(async () =>
                {
                for (int i = 0; i < Products.Count; i++)
                {
                    var product = Products[i];
                    try
                    {
                        if (IsEmptyProduct(product)) continue;

                        ((IProgress<string>)progress).Report($"Processing product {i + 1} of {Products.Count}: {product.Name}");

                        NormalizeProductData(product);
                        ValidateBarcode(product);

                        await _productService.CreateAsync(product);
                        successCount++;
                    }
                        catch (Exception ex)
                        {
                            errors.Add($"Error saving {product.Name}: {ex.Message}");
                        }
                    }
                });

                if (successCount > 0)
                {
                    MessageBox.Show($"Successfully saved {successCount} products.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    if (errors.Any())
                    {
                        MessageBox.Show($"Some products failed to save:\n\n{string.Join("\n", errors)}",
                            "Partial Success", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    DialogResult = true;
                }
                else if (errors.Any())
                {
                    MessageBox.Show($"Failed to save any products:\n\n{string.Join("\n", errors)}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error during save operation: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
            }
        }

        private Dictionary<int, List<string>> ValidateAllProducts()
        {
            var errors = new Dictionary<int, List<string>>();

            for (int i = 0; i < Products.Count; i++)
            {
                var product = Products[i];
                var productErrors = new List<string>();

                if (IsEmptyProduct(product)) continue;

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

                if (product.MinimumStock > product.CurrentStock)
                    productErrors.Add("Minimum stock cannot exceed current stock");

                if (!string.IsNullOrWhiteSpace(product.Speed))
                {
                    if (!decimal.TryParse(product.Speed, out _))
                    {
                        productErrors.Add("Speed must be a valid number");
                    }
                }

                if (productErrors.Any())
                    errors.Add(i, productErrors);
            }

            return errors;
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

        private void NormalizeProductData(ProductDTO product)
        {
            product.Name = product.Name?.Trim();
            product.Barcode = product.Barcode?.Trim();
            product.Description = product.Description?.Trim();
            product.Speed = product.Speed?.Trim();

            product.CurrentStock = Math.Max(0, product.CurrentStock);
            product.MinimumStock = Math.Max(0, product.MinimumStock);
            product.SalePrice = Math.Max(0, product.SalePrice);
            product.PurchasePrice = Math.Max(0, product.PurchasePrice);

            if (product.MinimumStock > product.CurrentStock)
                product.MinimumStock = product.CurrentStock;

            if (product.SalePrice < product.PurchasePrice)
                product.SalePrice = product.PurchasePrice;

            product.IsActive = true;
        }

        private void ValidateBarcode(ProductDTO product)
        {
            if (string.IsNullOrWhiteSpace(product.Barcode))
            {
                GenerateBarcode(product);
                return;
            }

            product.Barcode = new string(product.Barcode.Where(c => char.IsLetterOrDigit(c)).ToArray());

            if (product.Barcode.Length < 8)
            {
                GenerateBarcode(product);
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

                product.Barcode = $"{categoryPrefix}{timestamp}{randomDigits}";
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