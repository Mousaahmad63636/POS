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
using System.Data;
using System.Globalization;
using OfficeOpenXml;
using Microsoft.Win32;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Text;
using System.Threading;

namespace QuickTechSystems.WPF.ViewModels
{
    public class BulkProductViewModel : ViewModelBase
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IBarcodeService _barcodeService;
        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<CategoryDTO> _plantsHardscapeCategories;
        private ObservableCollection<CategoryDTO> _localImportedCategories;
        private ObservableCollection<CategoryDTO> _indoorOutdoorCategories;
        private ObservableCollection<CategoryDTO> _plantFamilyCategories;
        private ObservableCollection<CategoryDTO> _detailCategories;
        private bool? _dialogResult;
        private string _statusMessage;
        private bool _isSaving;
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

        public ObservableCollection<CategoryDTO> PlantsHardscapeCategories
        {
            get => _plantsHardscapeCategories;
            set => SetProperty(ref _plantsHardscapeCategories, value);
        }

        public ObservableCollection<CategoryDTO> LocalImportedCategories
        {
            get => _localImportedCategories;
            set => SetProperty(ref _localImportedCategories, value);
        }

        public ObservableCollection<CategoryDTO> IndoorOutdoorCategories
        {
            get => _indoorOutdoorCategories;
            set => SetProperty(ref _indoorOutdoorCategories, value);
        }

        public ObservableCollection<CategoryDTO> PlantFamilyCategories
        {
            get => _plantFamilyCategories;
            set => SetProperty(ref _plantFamilyCategories, value);
        }

        public ObservableCollection<CategoryDTO> DetailCategories
        {
            get => _detailCategories;
            set => SetProperty(ref _detailCategories, value);
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
        public ICommand ImportFromExcelCommand { get; }
        public ICommand AddOneRowCommand { get; }
        public ICommand CancelOperationCommand { get; }
        public ICommand GenerateBarcodeCommand { get; }

        public BulkProductViewModel(
            IProductService productService,
            ICategoryService categoryService,
            ISupplierService supplierService,
            IBarcodeService barcodeService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _barcodeService = barcodeService ?? throw new ArgumentNullException(nameof(barcodeService));

            _products = new ObservableCollection<ProductDTO>();
            _plantsHardscapeCategories = new ObservableCollection<CategoryDTO>();
            _localImportedCategories = new ObservableCollection<CategoryDTO>();
            _indoorOutdoorCategories = new ObservableCollection<CategoryDTO>();
            _plantFamilyCategories = new ObservableCollection<CategoryDTO>();
            _detailCategories = new ObservableCollection<CategoryDTO>();
            _validationErrors = new Dictionary<int, List<string>>();
            _statusMessage = string.Empty;
            _cancellationTokenSource = new CancellationTokenSource();

            AddOneRowCommand = new RelayCommand(_ => AddEmptyRows(1), _ => !IsSaving);
            SaveCommand = new AsyncRelayCommand(async _ => await SaveProductsAsync(), _ => !IsSaving);
            AddFiveRowsCommand = new RelayCommand(_ => AddEmptyRows(5), _ => !IsSaving);
            AddTenRowsCommand = new RelayCommand(_ => AddEmptyRows(10), _ => !IsSaving);
            ClearEmptyRowsCommand = new RelayCommand(_ => ClearEmptyRows(), _ => !IsSaving);
            ClearAllCommand = new RelayCommand(_ => ClearAllRows(), _ => !IsSaving);
            ImportFromExcelCommand = new AsyncRelayCommand(async _ => await ImportFromExcelAsync(), _ => !IsSaving);
            GenerateBarcodeCommand = new RelayCommand(GenerateBarcode, _ => !IsSaving);
            CancelOperationCommand = new RelayCommand(_ => CancelCurrentOperation(), _ => IsSaving);

            LoadInitialData();
            Products.Add(CreateEmptyProduct());

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
                CurrentStock = 0
            };
        }

        private void Product_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProductDTO.PlantsHardscapeId))
            {
                UpdatePlantsHardscapeName(sender as ProductDTO);
            }
            else if (e.PropertyName == nameof(ProductDTO.LocalImportedId))
            {
                UpdateLocalImportedName(sender as ProductDTO);
            }
            else if (e.PropertyName == nameof(ProductDTO.IndoorOutdoorId))
            {
                UpdateIndoorOutdoorName(sender as ProductDTO);
            }
            else if (e.PropertyName == nameof(ProductDTO.PlantFamilyId))
            {
                UpdatePlantFamilyName(sender as ProductDTO);
            }
            else if (e.PropertyName == nameof(ProductDTO.DetailId))
            {
                UpdateDetailName(sender as ProductDTO);
            }
        }

        private void UpdatePlantsHardscapeName(ProductDTO product)
        {
            if (product == null || !product.PlantsHardscapeId.HasValue || product.PlantsHardscapeId <= 0) return;

            var category = PlantsHardscapeCategories.FirstOrDefault(c => c.CategoryId == product.PlantsHardscapeId);
            if (category != null)
            {
                product.PlantsHardscapeName = category.Name;
            }
        }

        private void UpdateLocalImportedName(ProductDTO product)
        {
            if (product == null || !product.LocalImportedId.HasValue || product.LocalImportedId <= 0) return;

            var category = LocalImportedCategories.FirstOrDefault(c => c.CategoryId == product.LocalImportedId);
            if (category != null)
            {
                product.LocalImportedName = category.Name;
            }
        }

        private void UpdateIndoorOutdoorName(ProductDTO product)
        {
            if (product == null || !product.IndoorOutdoorId.HasValue || product.IndoorOutdoorId <= 0) return;

            var category = IndoorOutdoorCategories.FirstOrDefault(c => c.CategoryId == product.IndoorOutdoorId);
            if (category != null)
            {
                product.IndoorOutdoorName = category.Name;
            }
        }

        private void UpdatePlantFamilyName(ProductDTO product)
        {
            if (product == null || !product.PlantFamilyId.HasValue || product.PlantFamilyId <= 0) return;

            var category = PlantFamilyCategories.FirstOrDefault(c => c.CategoryId == product.PlantFamilyId);
            if (category != null)
            {
                product.PlantFamilyName = category.Name;
            }
        }

        private void UpdateDetailName(ProductDTO product)
        {
            if (product == null || !product.DetailId.HasValue || product.DetailId <= 0) return;

            var category = DetailCategories.FirstOrDefault(c => c.CategoryId == product.DetailId);
            if (category != null)
            {
                product.DetailName = category.Name;
            }
        }

        private async void LoadInitialData()
        {
            try
            {
                var categories = await _categoryService.GetActiveAsync();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var categoryList = categories.ToList();

                    PlantsHardscapeCategories = new ObservableCollection<CategoryDTO>(
                        FilterCategoriesByType(categoryList, "PlantsHardscape")
                    );

                    LocalImportedCategories = new ObservableCollection<CategoryDTO>(
                        FilterCategoriesByType(categoryList, "LocalImported")
                    );

                    IndoorOutdoorCategories = new ObservableCollection<CategoryDTO>(
                        FilterCategoriesByType(categoryList, "IndoorOutdoor")
                    );

                    PlantFamilyCategories = new ObservableCollection<CategoryDTO>(
                        FilterCategoriesByType(categoryList, "PlantFamily")
                    );

                    DetailCategories = new ObservableCollection<CategoryDTO>(
                        FilterCategoriesByType(categoryList, "Detail")
                    );

                    Debug.WriteLine($"Loaded {categories.Count()} categories");
                    Debug.WriteLine($"Categorized: PH={PlantsHardscapeCategories.Count}, LI={LocalImportedCategories.Count}, IO={IndoorOutdoorCategories.Count}, PF={PlantFamilyCategories.Count}, D={DetailCategories.Count}");
                });
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading data: {ex.Message}");
                Debug.WriteLine($"Error loading initial data: {ex}");
            }
        }

        private List<CategoryDTO> FilterCategoriesByType(List<CategoryDTO> categories, string categoryType)
        {
            var filteredCategories = new List<CategoryDTO>();

            switch (categoryType)
            {
                case "PlantsHardscape":
                    filteredCategories = categories.Where(c =>
                        c.Name.Contains("Plant", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Hardscape", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Landscape", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Garden", StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                    break;

                case "LocalImported":
                    filteredCategories = categories.Where(c =>
                        c.Name.Contains("Local", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Import", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Domestic", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Foreign", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("International", StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                    break;

                case "IndoorOutdoor":
                    filteredCategories = categories.Where(c =>
                        c.Name.Contains("Indoor", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Outdoor", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Interior", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Exterior", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("House", StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                    break;

                case "PlantFamily":
                    filteredCategories = categories.Where(c =>
                        c.Name.Contains("Flower", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Foliage", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Succulent", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Tree", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Shrub", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Herb", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Fern", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Vine", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Grass", StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                    break;

                case "Detail":
                    filteredCategories = categories.Where(c =>
                        c.Name.Contains("Season", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Perennial", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Annual", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Rare", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Common", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Special", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Premium", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("Standard", StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                    break;
            }

            if (!filteredCategories.Any())
            {
                return categories.Take(Math.Min(10, categories.Count)).ToList();
            }

            return filteredCategories.OrderBy(c => c.Name).ToList();
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

                StatusMessage = "Preparing products for save...";
                foreach (var product in productsToSave)
                {
                    token.ThrowIfCancellationRequested();
                    NormalizeProductData(product);
                    ValidateBarcode(product);
                }

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
                    var error = GetDetailedErrorMessage(ex);
                    errors.Add($"Batch save failed: {error}");
                    Debug.WriteLine($"Batch save error: {error}");

                    var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        return MessageBox.Show(
                            "Batch save failed. Would you like to try saving products individually?\n\n" +
                            "This may succeed for some products but not all.",
                            "Error", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    });

                    if (result == MessageBoxResult.Yes)
                    {
                        for (int i = 0; i < productsToSave.Count; i++)
                        {
                            try
                            {
                                token.ThrowIfCancellationRequested();

                                var product = productsToSave[i];
                                ((IProgress<string>)progress).Report($"Processing product {i + 1} of {productsToSave.Count}: {product.Name}");

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

            var currentEx = ex;
            while (currentEx.InnerException != null)
            {
                currentEx = currentEx.InnerException;
                sb.Append($"\n→ {currentEx.Message}");
            }

            return sb.ToString();
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

                if (product.SalePrice <= 0)
                    productErrors.Add("Sale price must be greater than zero");

                if (product.PurchasePrice <= 0)
                    productErrors.Add("Purchase price must be greater than zero");

                if (product.CurrentStock < 0)
                    productErrors.Add("Current stock cannot be negative");

                if (product.SalePrice < product.PurchasePrice)
                    productErrors.Add("Sale price should not be less than purchase price");

                if (!string.IsNullOrWhiteSpace(product.Barcode))
                {
                    if (product.Barcode.Length < 4)
                        productErrors.Add("Barcode must be at least 4 characters long");

                    if (!product.Barcode.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                        productErrors.Add("Barcode can only contain letters, numbers, hyphens, and underscores");
                }

                if (!string.IsNullOrWhiteSpace(product.Barcode))
                {
                    var duplicateIndices = new List<int>();

                    for (int j = 0; j < Products.Count; j++)
                    {
                        if (j != i && !IsEmptyProduct(Products[j]) &&
                            !string.IsNullOrWhiteSpace(Products[j].Barcode) &&
                            Products[j].Barcode.Equals(product.Barcode, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicateIndices.Add(j + 1);
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

        private void ValidateBarcode(ProductDTO product)
        {
            if (string.IsNullOrWhiteSpace(product.Barcode))
            {
                GenerateBarcodeForProduct(product);
                return;
            }

            product.Barcode = new string(product.Barcode.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());

            if (string.IsNullOrWhiteSpace(product.Barcode) || product.Barcode.Length < 4)
            {
                GenerateBarcodeForProduct(product);
            }
            else if (product.BarcodeImage == null)
            {
                product.BarcodeImage = _barcodeService.GenerateBarcode(product.Barcode);
            }
        }

        private void GenerateBarcodeForProduct(ProductDTO product)
        {
            var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
            var random = new Random();
            var randomDigits = random.Next(1000, 9999).ToString();
            var categoryPrefix = (product.CategoryId > 0 ? product.CategoryId.ToString() : "001").PadLeft(3, '0');

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

        private void GenerateBarcode(object? parameter)
        {
            try
            {
                if (parameter is not ProductDTO product) return;

                var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                var random = new Random();
                var randomDigits = random.Next(1000, 9999).ToString();
                var categoryPrefix = (product.CategoryId > 0 ? product.CategoryId.ToString() : "001").PadLeft(3, '0');

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

        private void NormalizeProductData(ProductDTO product)
        {
            product.Name = product.Name?.Trim() ?? string.Empty;
            product.Barcode = product.Barcode?.Trim() ?? string.Empty;
            product.Description = product.Description?.Trim();

            product.CurrentStock = Math.Max(0, product.CurrentStock);
            product.SalePrice = Math.Max(0, product.SalePrice);
            product.PurchasePrice = Math.Max(0, product.PurchasePrice);

            if (product.SalePrice < product.PurchasePrice)
                product.SalePrice = product.PurchasePrice;

            if (product.CreatedAt == default)
                product.CreatedAt = DateTime.Now;

            product.UpdatedAt = DateTime.Now;
            product.IsActive = true;
            product.CategoryId = 1;

            UpdatePlantsHardscapeName(product);
            UpdateLocalImportedName(product);
            UpdateIndoorOutdoorName(product);
            UpdatePlantFamilyName(product);
            UpdateDetailName(product);
        }

        private bool IsEmptyProduct(ProductDTO product)
        {
            return string.IsNullOrWhiteSpace(product.Name) &&
                   product.PurchasePrice == 0 &&
                   product.SalePrice == 0 &&
                   product.CurrentStock == 0 &&
                   !product.PlantsHardscapeId.HasValue &&
                   !product.LocalImportedId.HasValue &&
                   !product.IndoorOutdoorId.HasValue &&
                   !product.PlantFamilyId.HasValue &&
                   !product.DetailId.HasValue;
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
                    var lines = File.ReadAllLines(filePath);
                    if (lines.Length <= 1)
                    {
                        throw new InvalidOperationException("CSV file contains no data rows.");
                    }

                    var headerLine = lines[0];
                    var headers = SplitCsvLine(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToArray();
                    var columnMappings = new Dictionary<string, int>();

                    for (int i = 0; i < headers.Length; i++)
                    {
                        columnMappings[headers[i]] = i;
                    }

                    var requiredColumns = new[] { "name", "purchase price", "sale price" };
                    var missingColumns = requiredColumns.Where(col => !columnMappings.Keys.Any(key => key.Contains(col))).ToList();

                    if (missingColumns.Any())
                    {
                        throw new InvalidOperationException($"Missing required columns: {string.Join(", ", missingColumns)}");
                    }

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

                            foreach (var mapping in columnMappings)
                            {
                                if (mapping.Value >= values.Length) continue;

                                var value = values[mapping.Value]?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(value)) continue;

                                ProcessProductValue(product, mapping.Key, value, rowIndex + 1, errors);
                            }

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
                    ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                    using (var package = new ExcelPackage(new FileInfo(filePath)))
                    {
                        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                        if (worksheet == null)
                        {
                            throw new InvalidOperationException("No worksheet found in the Excel file.");
                        }

                        int rowCount = worksheet.Dimension?.Rows ?? 0;
                        int colCount = worksheet.Dimension?.Columns ?? 0;

                        if (rowCount <= 1)
                        {
                            throw new InvalidOperationException("Excel file contains no data rows.");
                        }

                        var columnMappings = new Dictionary<string, int>();
                        for (int col = 1; col <= colCount; col++)
                        {
                            var headerValue = worksheet.Cells[1, col].Text?.Trim();
                            if (!string.IsNullOrEmpty(headerValue))
                            {
                                columnMappings[headerValue.ToLowerInvariant()] = col;
                            }
                        }

                        var requiredColumns = new[] { "name", "purchase price", "sale price" };
                        var missingColumns = requiredColumns.Where(col => !columnMappings.Keys.Any(key => key.Contains(col))).ToList();

                        if (missingColumns.Any())
                        {
                            throw new InvalidOperationException($"Missing required columns: {string.Join(", ", missingColumns)}");
                        }

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

                                foreach (var mapping in columnMappings)
                                {
                                    var value = worksheet.Cells[row, mapping.Value].Text?.Trim();
                                    if (string.IsNullOrEmpty(value)) continue;

                                    ProcessProductValue(product, mapping.Key, value, row, errors);
                                }

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
                case var col when col.Contains("plants") || col.Contains("hardscape"):
                    var plantsHardscape = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        PlantsHardscapeCategories.FirstOrDefault(c => c.Name.Equals(value, StringComparison.OrdinalIgnoreCase)));
                    if (plantsHardscape != null)
                    {
                        product.PlantsHardscapeId = plantsHardscape.CategoryId;
                        product.PlantsHardscapeName = plantsHardscape.Name;
                    }
                    break;
                case var col when col.Contains("local") || col.Contains("imported"):
                    var localImported = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        LocalImportedCategories.FirstOrDefault(c => c.Name.Equals(value, StringComparison.OrdinalIgnoreCase)));
                    if (localImported != null)
                    {
                        product.LocalImportedId = localImported.CategoryId;
                        product.LocalImportedName = localImported.Name;
                    }
                    break;
                case var col when col.Contains("indoor") || col.Contains("outdoor"):
                    var indoorOutdoor = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        IndoorOutdoorCategories.FirstOrDefault(c => c.Name.Equals(value, StringComparison.OrdinalIgnoreCase)));
                    if (indoorOutdoor != null)
                    {
                        product.IndoorOutdoorId = indoorOutdoor.CategoryId;
                        product.IndoorOutdoorName = indoorOutdoor.Name;
                    }
                    break;
                case var col when col.Contains("plant family"):
                    var plantFamily = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        PlantFamilyCategories.FirstOrDefault(c => c.Name.Equals(value, StringComparison.OrdinalIgnoreCase)));
                    if (plantFamily != null)
                    {
                        product.PlantFamilyId = plantFamily.CategoryId;
                        product.PlantFamilyName = plantFamily.Name;
                    }
                    break;
                case var col when col.Contains("detail"):
                    var detail = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        DetailCategories.FirstOrDefault(c => c.Name.Equals(value, StringComparison.OrdinalIgnoreCase)));
                    if (detail != null)
                    {
                        product.DetailId = detail.CategoryId;
                        product.DetailName = detail.Name;
                    }
                    break;
            }
        }

        private bool IsValidImportedProduct(ProductDTO product, int rowIndex, List<string> errors)
        {
            if (!string.IsNullOrWhiteSpace(product.Name) &&
                product.PurchasePrice > 0 &&
                product.SalePrice > 0)
            {
                return true;
            }
            else if (!string.IsNullOrWhiteSpace(product.Name))
            {
                var missingFields = new List<string>();
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
                    if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentValue.Append('"');
                        i++;
                    }
                    else
                    {
                        insideQuotes = !insideQuotes;
                    }
                }
                else if (c == ',' && !insideQuotes)
                {
                    result.Add(currentValue.ToString());
                    currentValue.Clear();
                }
                else
                {
                    currentValue.Append(c);
                }
            }

            result.Add(currentValue.ToString());

            return result.ToArray();
        }

        private void UpdateUIWithImportedProducts(List<ProductDTO> importedProducts, List<string> errors)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (importedProducts.Any())
                {
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
                        ClearEmptyRows();
                    }

                    foreach (var product in importedProducts)
                    {
                        Products.Add(product);
                    }

                    if (!Products.Any(IsEmptyProduct))
                    {
                        Products.Add(CreateEmptyProduct());
                    }

                    MessageBox.Show(
                        $"Successfully imported {importedProducts.Count} products." +
                        (errors.Any() ? $"\n\nWarning: {errors.Count} rows had errors." : ""),
                        "Import Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

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

                try
                {
                    _operationLock?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during operation lock disposal: {ex.Message}");
                }

                foreach (var product in Products)
                {
                    product.PropertyChanged -= Product_PropertyChanged;
                }

                base.Dispose();
            }
        }
    }
}