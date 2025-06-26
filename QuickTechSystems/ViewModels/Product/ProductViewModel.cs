﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Collections.Generic;

namespace QuickTechSystems.ViewModels.Product
{
    public class ProductViewModel : ViewModelBase
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private readonly ISupplierInvoiceService _supplierInvoiceService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;

        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private ObservableCollection<SupplierInvoiceDTO> _supplierInvoices;
        private ProductDTO? _selectedProduct;
        private SupplierInvoiceDTO? _selectedSupplierInvoice;
        private bool _isEditing;
        private bool _isLoading;
        private string _loadingMessage = string.Empty;
        private Dictionary<string, string> _validationErrors;
        private Action<EntityChangedEvent<ProductDTO>> _productChangedHandler;
        private decimal _transferQuantity;
        private int _transferBoxes;
        private bool _isIndividualTransfer = true;
        private bool _isBoxTransfer;
        private string _transferValidationMessage = string.Empty;
        private bool _hasTransferValidationMessage;
        private string _searchText = string.Empty;

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

        public ObservableCollection<SupplierInvoiceDTO> SupplierInvoices
        {
            get => _supplierInvoices;
            set => SetProperty(ref _supplierInvoices, value);
        }

        public ProductDTO? SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (_selectedProduct != null)
                {
                    _selectedProduct.PropertyChanged -= OnSelectedProductPropertyChanged;
                }

                if (SetProperty(ref _selectedProduct, value))
                {
                    IsEditing = value != null;
                    ResetTransferValues();
                    ClearTransferValidation();
                    OnPropertyChanged(nameof(AvailableBoxes));

                    if (value != null)
                    {
                        value.PropertyChanged += OnSelectedProductPropertyChanged;
                        _ = LoadSupplierInvoicesAsync();
                    }
                    else
                    {
                        SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>();
                        SelectedSupplierInvoice = null;
                    }
                }
            }
        }

        public SupplierInvoiceDTO? SelectedSupplierInvoice
        {
            get => _selectedSupplierInvoice;
            set => SetProperty(ref _selectedSupplierInvoice, value);
        }

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        public Dictionary<string, string> ValidationErrors
        {
            get => _validationErrors;
            set => SetProperty(ref _validationErrors, value);
        }

        public decimal TransferQuantity
        {
            get => _transferQuantity;
            set => SetProperty(ref _transferQuantity, value);
        }

        public int TransferBoxes
        {
            get => _transferBoxes;
            set
            {
                if (SetProperty(ref _transferBoxes, value))
                {
                    ValidateBoxTransfer();
                    OnPropertyChanged(nameof(BoxTransferSummary));
                }
            }
        }

        public bool IsIndividualTransfer
        {
            get => _isIndividualTransfer;
            set
            {
                if (SetProperty(ref _isIndividualTransfer, value))
                {
                    if (value) IsBoxTransfer = false;
                    ClearTransferValidation();
                    ResetTransferValues();
                    OnPropertyChanged(nameof(TransferButtonText));
                }
            }
        }

        public bool IsBoxTransfer
        {
            get => _isBoxTransfer;
            set
            {
                if (SetProperty(ref _isBoxTransfer, value))
                {
                    if (value) IsIndividualTransfer = false;
                    ClearTransferValidation();
                    ResetTransferValues();
                    OnPropertyChanged(nameof(TransferButtonText));
                    OnPropertyChanged(nameof(AvailableBoxes));
                }
            }
        }

        public string TransferValidationMessage
        {
            get => _transferValidationMessage;
            set => SetProperty(ref _transferValidationMessage, value);
        }

        public bool HasTransferValidationMessage
        {
            get => _hasTransferValidationMessage;
            set => SetProperty(ref _hasTransferValidationMessage, value);
        }

        public decimal AvailableBoxes
        {
            get
            {
                if (SelectedProduct?.ItemsPerBox > 0)
                    return Math.Floor(SelectedProduct.Storehouse / SelectedProduct.ItemsPerBox);
                return 0;
            }
        }

        public string BoxTransferSummary
        {
            get
            {
                if (SelectedProduct?.ItemsPerBox > 0 && TransferBoxes > 0)
                {
                    var totalItems = TransferBoxes * SelectedProduct.ItemsPerBox;
                    return $"= {totalItems} individual items";
                }
                return string.Empty;
            }
        }

        public string TransferButtonText
        {
            get => IsBoxTransfer ? "Transfer Boxes" : "Transfer Items";
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    PerformSearch();
                }
            }
        }

        public ICommand AddProductCommand { get; }
        public ICommand SaveProductCommand { get; }
        public ICommand DeleteProductCommand { get; }
        public ICommand TransferFromStorehouseCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand GenerateBarcodeCommand { get; }
        public ICommand ResetTransferCommand { get; }
        public ICommand SelectProductCommand { get; }
        public ProductViewModel(
         IProductService productService,
         ICategoryService categoryService,
         ISupplierService supplierService,
         ISupplierInvoiceService supplierInvoiceService,
         IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _productService = productService;
            _categoryService = categoryService;
            _supplierService = supplierService;
            _supplierInvoiceService = supplierInvoiceService;
            _products = new ObservableCollection<ProductDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();
            _supplierInvoices = new ObservableCollection<SupplierInvoiceDTO>();
            _validationErrors = new Dictionary<string, string>();
            _productChangedHandler = HandleProductChanged;

            AddProductCommand = new RelayCommand(_ => AddProduct());
            SaveProductCommand = new AsyncRelayCommand(async _ => await SaveProductAsync());
            DeleteProductCommand = new AsyncRelayCommand(async _ => await DeleteProductAsync());
            TransferFromStorehouseCommand = new AsyncRelayCommand(async _ => await TransferFromStorehouseAsync());
            RefreshCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            SearchCommand = new AsyncRelayCommand(async _ => await SearchProductsAsync());
            GenerateBarcodeCommand = new RelayCommand(GenerateBarcode);
            ResetTransferCommand = new RelayCommand(_ => ResetTransfer());
            SelectProductCommand = new RelayCommand(SelectProduct); // Add this line

            _ = LoadDataAsync();
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe(_productChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe(_productChangedHandler);
        }

        private async void HandleProductChanged(EntityChangedEvent<ProductDTO> evt)
        {
            await LoadDataAsync();
        }
        private void SelectProduct(object parameter)
        {
            if (parameter is ProductDTO product)
            {
                // First, reset IsSelected for all products
                foreach (var p in Products)
                {
                    p.IsSelected = false;
                }

                // Set the selected product
                product.IsSelected = true;
                SelectedProduct = product;
            }
        }

        protected override async Task LoadDataAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                IsLoading = true;
                LoadingMessage = "Loading products...";

                var productsTask = _productService.GetAllAsync();
                var categoriesTask = _categoryService.GetProductCategoriesAsync();
                var suppliersTask = _supplierService.GetActiveAsync();

                await Task.WhenAll(productsTask, categoriesTask, suppliersTask);

                var products = await productsTask;
                var categories = await categoriesTask;
                var suppliers = await suppliersTask;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Products = new ObservableCollection<ProductDTO>(products);
                    Categories = new ObservableCollection<CategoryDTO>(categories);
                    Suppliers = new ObservableCollection<SupplierDTO>(suppliers);

                    OnPropertyChanged(nameof(Products));
                    OnPropertyChanged(nameof(Categories));
                    OnPropertyChanged(nameof(Suppliers));
                });
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error loading data: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async void OnSelectedProductPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProductDTO.SupplierId))
            {
                await LoadSupplierInvoicesAsync();
            }
        }

        private async Task LoadSupplierInvoicesAsync()
        {
            if (SelectedProduct?.SupplierId == null)
            {
                SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>();
                return;
            }

            try
            {
                var invoices = await _supplierInvoiceService.GetBySupplierAsync(SelectedProduct.SupplierId.Value);
                var draftInvoices = invoices.Where(i => i.Status == "Draft").ToList();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>(draftInvoices);
                });
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error loading invoices: {ex.Message}");
            }
        }

        public void AddProduct()
        {
            var newProduct = new ProductDTO
            {
                IsActive = true,
                CreatedAt = DateTime.Now,
                ItemsPerBox = 1,
                MinimumStock = 0,
                CurrentStock = 0,
                Storehouse = 0
            };

            SelectedProduct = newProduct;
            SelectedSupplierInvoice = null;
        }

        private async Task SaveProductAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Save operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (!ValidateProduct(SelectedProduct))
                    return;

                IsLoading = true;
                LoadingMessage = SelectedProduct.ProductId == 0 ?
                    "Creating new product..." :
                    "Updating product...";

                var productBeingSaved = SelectedProduct;
                bool isNew = productBeingSaved.ProductId == 0;

                if (isNew)
                {
                    productBeingSaved.CreatedAt = DateTime.Now;

                    if (productBeingSaved.ItemsPerBox > 0)
                    {
                        if (productBeingSaved.BoxPurchasePrice == 0 && productBeingSaved.PurchasePrice > 0)
                            productBeingSaved.BoxPurchasePrice = productBeingSaved.PurchasePrice * productBeingSaved.ItemsPerBox;
                        if (productBeingSaved.BoxSalePrice == 0 && productBeingSaved.SalePrice > 0)
                            productBeingSaved.BoxSalePrice = productBeingSaved.SalePrice * productBeingSaved.ItemsPerBox;
                        if (productBeingSaved.BoxWholesalePrice == 0 && productBeingSaved.WholesalePrice > 0)
                            productBeingSaved.BoxWholesalePrice = productBeingSaved.WholesalePrice * productBeingSaved.ItemsPerBox;
                    }

                    var savedProduct = await _productService.CreateAsync(productBeingSaved);

                    if (SelectedSupplierInvoice != null)
                    {
                        await LinkProductToInvoiceAsync(savedProduct);
                    }

                    await ShowSuccessMessage("Product created successfully.");

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Products.Add(savedProduct);
                    });
                }
                else
                {
                    productBeingSaved.UpdatedAt = DateTime.Now;

                    if (productBeingSaved.ItemsPerBox > 0)
                    {
                        if (productBeingSaved.BoxPurchasePrice == 0 && productBeingSaved.PurchasePrice > 0)
                            productBeingSaved.BoxPurchasePrice = productBeingSaved.PurchasePrice * productBeingSaved.ItemsPerBox;
                        if (productBeingSaved.BoxSalePrice == 0 && productBeingSaved.SalePrice > 0)
                            productBeingSaved.BoxSalePrice = productBeingSaved.SalePrice * productBeingSaved.ItemsPerBox;
                        if (productBeingSaved.BoxWholesalePrice == 0 && productBeingSaved.WholesalePrice > 0)
                            productBeingSaved.BoxWholesalePrice = productBeingSaved.WholesalePrice * productBeingSaved.ItemsPerBox;
                    }

                    await _productService.UpdateAsync(productBeingSaved);
                    await ShowSuccessMessage("Product updated successfully.");

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        int index = -1;
                        for (int i = 0; i < Products.Count; i++)
                        {
                            if (Products[i].ProductId == productBeingSaved.ProductId)
                            {
                                index = i;
                                break;
                            }
                        }

                        if (index >= 0)
                        {
                            Products[index] = productBeingSaved;
                        }
                    });
                }

                SelectedProduct = null;
                SelectedSupplierInvoice = null;
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error saving product: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task LinkProductToInvoiceAsync(ProductDTO product)
        {
            if (SelectedSupplierInvoice == null || product.PurchasePrice <= 0) return;

            try
            {
                decimal totalQuantity = product.CurrentStock + product.Storehouse;
                decimal totalAmount = totalQuantity * product.PurchasePrice;

                var invoiceDetail = new SupplierInvoiceDetailDTO
                {
                    SupplierInvoiceId = SelectedSupplierInvoice.SupplierInvoiceId,
                    ProductId = product.ProductId,
                    Quantity = totalQuantity,
                    PurchasePrice = product.PurchasePrice,
                    TotalPrice = totalAmount,
                    BoxBarcode = product.BoxBarcode ?? string.Empty,
                    NumberOfBoxes = product.NumberOfBoxes,
                    ItemsPerBox = product.ItemsPerBox,
                    BoxPurchasePrice = product.BoxPurchasePrice,
                    BoxSalePrice = product.BoxSalePrice,
                    CurrentStock = product.CurrentStock,
                    Storehouse = product.Storehouse,
                    SalePrice = product.SalePrice,
                    WholesalePrice = product.WholesalePrice,
                    BoxWholesalePrice = product.BoxWholesalePrice,
                    MinimumStock = product.MinimumStock,
                    CategoryName = product.CategoryName,
                    SupplierName = product.SupplierName
                };

                await _supplierInvoiceService.AddProductToInvoiceAsync(invoiceDetail);
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Product saved but could not link to invoice: {ex.Message}");
            }
        }

        private async Task DeleteProductAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Delete operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (SelectedProduct == null) return;

                var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    return MessageBox.Show("Are you sure you want to delete this product?",
                        "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                });

                if (result == MessageBoxResult.Yes)
                {
                    IsLoading = true;
                    LoadingMessage = "Deleting product...";

                    int productId = SelectedProduct.ProductId;

                    await _productService.DeleteAsync(productId);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var productToRemove = Products.FirstOrDefault(p => p.ProductId == productId);
                        if (productToRemove != null)
                        {
                            Products.Remove(productToRemove);
                        }
                    });

                    SelectedProduct = null;
                    await ShowSuccessMessage("Product deleted successfully.");
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("transactions") || ex.Message.Contains("associated"))
                {
                    ShowTemporaryErrorMessage("This product has associated transactions and cannot be deleted. Consider marking it as inactive instead.");

                    var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        return MessageBox.Show("Would you like to mark this product as inactive instead?",
                            "Mark as Inactive", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    });

                    if (result == MessageBoxResult.Yes && SelectedProduct != null)
                    {
                        SelectedProduct.IsActive = false;
                        await SaveProductAsync();
                    }
                }
                else
                {
                    ShowTemporaryErrorMessage($"Error deleting product: {ex.Message}");
                }
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task TransferFromStorehouseAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Transfer operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (SelectedProduct == null)
                {
                    ShowTemporaryErrorMessage("Please select a product first.");
                    return;
                }

                ClearTransferValidation();

                decimal actualTransferQuantity;
                string transferType;

                if (IsIndividualTransfer)
                {
                    if (TransferQuantity <= 0)
                    {
                        SetTransferValidation("Transfer quantity must be greater than zero.");
                        return;
                    }

                    if (TransferQuantity > SelectedProduct.Storehouse)
                    {
                        SetTransferValidation($"Insufficient quantity in storehouse. Available: {SelectedProduct.Storehouse}");
                        return;
                    }

                    actualTransferQuantity = TransferQuantity;
                    transferType = $"Individual items transfer: {TransferQuantity} items";
                }
                else
                {
                    if (TransferBoxes <= 0)
                    {
                        SetTransferValidation("Number of boxes must be greater than zero.");
                        return;
                    }

                    if (SelectedProduct.ItemsPerBox <= 0)
                    {
                        SetTransferValidation("Items per box must be configured before box transfers.");
                        return;
                    }

                    if (TransferBoxes > AvailableBoxes)
                    {
                        SetTransferValidation($"Insufficient boxes in storehouse. Available: {AvailableBoxes} boxes");
                        return;
                    }

                    actualTransferQuantity = TransferBoxes * SelectedProduct.ItemsPerBox;
                    transferType = $"Box transfer: {TransferBoxes} boxes ({actualTransferQuantity} items)";
                }

                IsLoading = true;
                LoadingMessage = "Processing transfer...";

                var success = await _productService.TransferFromStorehouseAsync(SelectedProduct.ProductId, actualTransferQuantity);

                if (success)
                {
                    SelectedProduct.Storehouse -= actualTransferQuantity;
                    SelectedProduct.CurrentStock += actualTransferQuantity;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        int index = -1;
                        for (int i = 0; i < Products.Count; i++)
                        {
                            if (Products[i].ProductId == SelectedProduct.ProductId)
                            {
                                index = i;
                                break;
                            }
                        }

                        if (index >= 0)
                        {
                            Products[index] = SelectedProduct;
                        }

                        OnPropertyChanged(nameof(AvailableBoxes));
                    });

                    ResetTransferValues();
                    await ShowSuccessMessage($"Transfer completed successfully. {transferType}");
                }
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error transferring from storehouse: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task SearchProductsAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                await LoadDataAsync();
                return;
            }

            try
            {
                IsLoading = true;
                LoadingMessage = "Searching products...";

                var products = await _productService.SearchByNameAsync(SearchText);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Products = new ObservableCollection<ProductDTO>(products);
                });
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error searching products: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void PerformSearch()
        {
            _ = SearchProductsAsync();
        }

        private void GenerateBarcode(object parameter)
        {
            if (SelectedProduct != null)
            {
                var random = new Random();
                // Generate random length between 4 and 10 digits
                var length = random.Next(4, 11); // 4 to 10 inclusive

                // Generate barcode with the determined length
                var minValue = (long)Math.Pow(10, length - 1); // e.g., 1000 for 4 digits
                var maxValue = (long)Math.Pow(10, length) - 1;  // e.g., 9999 for 4 digits

                var barcode = random.NextInt64(minValue, maxValue + 1).ToString();

                if (parameter?.ToString() == "Box")
                {
                    SelectedProduct.BoxBarcode = barcode;
                }
                else
                {
                    SelectedProduct.Barcode = barcode;
                }
            }
        }

        private void ResetTransfer()
        {
            ResetTransferValues();
            ClearTransferValidation();
            IsIndividualTransfer = true;
            IsBoxTransfer = false;
        }

        private void ResetTransferValues()
        {
            TransferQuantity = 0;
            TransferBoxes = 0;
        }

        private void ValidateBoxTransfer()
        {
            if (!IsBoxTransfer || SelectedProduct == null) return;

            ClearTransferValidation();

            if (SelectedProduct.ItemsPerBox <= 0)
            {
                SetTransferValidation("Items per box must be configured before box transfers.");
                return;
            }

            if (TransferBoxes > AvailableBoxes)
            {
                SetTransferValidation($"Insufficient boxes in storehouse. Available: {AvailableBoxes} boxes");
                return;
            }

            if (TransferBoxes < 0)
            {
                SetTransferValidation("Number of boxes cannot be negative.");
            }
        }

        private void ClearTransferValidation()
        {
            TransferValidationMessage = string.Empty;
            HasTransferValidationMessage = false;
        }

        private void SetTransferValidation(string message)
        {
            TransferValidationMessage = message;
            HasTransferValidationMessage = !string.IsNullOrEmpty(message);
        }

        private bool ValidateProduct(ProductDTO? product)
        {
            ValidationErrors.Clear();

            if (product == null)
            {
                ValidationErrors.Add("General", "No product selected.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(product.Name))
                ValidationErrors.Add("Name", "Product name is required.");

            if (product.Name?.Length > 200)
                ValidationErrors.Add("Name", "Product name cannot exceed 200 characters.");

            if (string.IsNullOrWhiteSpace(product.Barcode))
                ValidationErrors.Add("Barcode", "Barcode is required.");

            if (product.CategoryId <= 0)
                ValidationErrors.Add("Category", "Please select a category.");

            if (product.PurchasePrice < 0)
                ValidationErrors.Add("PurchasePrice", "Purchase price cannot be negative.");

            if (product.SalePrice < 0)
                ValidationErrors.Add("SalePrice", "Sale price cannot be negative.");

            if (product.CurrentStock < 0)
                ValidationErrors.Add("CurrentStock", "Current stock cannot be negative.");

            if (product.Storehouse < 0)
                ValidationErrors.Add("Storehouse", "Storehouse quantity cannot be negative.");

            OnPropertyChanged(nameof(ValidationErrors));

            if (ValidationErrors.Count > 0)
            {
                ShowValidationErrors(ValidationErrors.Values.ToList());
                return false;
            }

            return true;
        }

        private void ShowValidationErrors(List<string> errors)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(string.Join("\n", errors), "Validation Errors",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        private async Task ShowSuccessMessage(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void ShowTemporaryErrorMessage(string message)
        {
            LoadingMessage = message;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });

            Task.Run(async () =>
            {
                await Task.Delay(5000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (LoadingMessage == message)
                    {
                        LoadingMessage = string.Empty;
                    }
                });
            });
        }

        public override void Dispose()
        {
            if (!_isDisposed)
            {
                if (_selectedProduct != null)
                {
                    _selectedProduct.PropertyChanged -= OnSelectedProductPropertyChanged;
                }

                _operationLock?.Dispose();
                UnsubscribeFromEvents();
                _isDisposed = true;
            }

            base.Dispose();
        }
    }
}