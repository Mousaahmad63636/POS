using System;
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
        private bool _isAutoFilling = false;
        private bool _suppressPropertyChangeEvents = false;
        // Core collections and properties
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
        private string _searchText = string.Empty;

        // Transfer properties
        private decimal _transferQuantity;
        private int _transferBoxes;
        private bool _isIndividualTransfer = true;
        private bool _isBoxTransfer;
        private string _transferValidationMessage = string.Empty;
        private bool _hasTransferValidationMessage;

        // Product matching and validation properties
        private ObservableCollection<ProductDTO> _matchingProducts;
        private ProductDTO? _selectedMatchingProduct;
        private bool _showMatchingProducts;
        private string _barcodeValidationMessage = string.Empty;
        private bool _hasBarcodeValidation;
        private bool _isExistingProduct;
        private ProductDTO? _originalProduct;
        private decimal _newQuantity;
        private decimal _newPurchasePrice;

        // Debouncing and cancellation
        private Timer? _searchTimer;
        private Timer? _barcodeTimer;
        private CancellationTokenSource? _searchCancellationTokenSource;
        private CancellationTokenSource? _barcodeCancellationTokenSource;
        private readonly object _searchLock = new object();
        private readonly object _barcodeLock = new object();
        private readonly object _databaseLock = new object();
        private string _pendingSearchTerm = string.Empty;
        private string _pendingBarcode = string.Empty;
        private bool _isEditingExistingProduct = false;

        // Properties
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
                    _selectedProduct.PropertyChanged -= OnNewProductPropertyChanged;
                }

                if (SetProperty(ref _selectedProduct, value))
                {
                    IsEditing = value != null;
                    ResetTransferValues();
                    ClearTransferValidation();

                    // Only clear matching products and barcode validation if not editing existing product
                    if (!_isEditingExistingProduct)
                    {
                        ClearMatchingProducts();
                        ClearBarcodeValidation();
                        CancelSearchOperations();
                    }

                    OnPropertyChanged(nameof(AvailableBoxes));

                    if (value != null)
                    {
                        value.PropertyChanged += OnSelectedProductPropertyChanged;

                        // Only subscribe to new product events if it's actually a new product (ProductId == 0)
                        // AND we're not editing an existing product from the list
                        // AND we're not in auto-fill mode
                        if (value.ProductId == 0 && !_isEditingExistingProduct && !IsExistingProduct)
                        {
                            value.PropertyChanged += OnNewProductPropertyChanged;
                        }

                        _ = LoadSupplierInvoicesAsync();
                    }
                    else
                    {
                        SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>();
                        SelectedSupplierInvoice = null;
                    }

                    // Reset the flag after setting the product
                    _isEditingExistingProduct = false;
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

        // Transfer properties
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

        // Product matching properties
        public ObservableCollection<ProductDTO> MatchingProducts
        {
            get => _matchingProducts;
            set => SetProperty(ref _matchingProducts, value);
        }

        public ProductDTO? SelectedMatchingProduct
        {
            get => _selectedMatchingProduct;
            set => SetProperty(ref _selectedMatchingProduct, value);
        }

        public bool ShowMatchingProducts
        {
            get => _showMatchingProducts;
            set => SetProperty(ref _showMatchingProducts, value);
        }

        public string BarcodeValidationMessage
        {
            get => _barcodeValidationMessage;
            set => SetProperty(ref _barcodeValidationMessage, value);
        }

        public bool HasBarcodeValidation
        {
            get => _hasBarcodeValidation;
            set => SetProperty(ref _hasBarcodeValidation, value);
        }

        public bool IsExistingProduct
        {
            get => _isExistingProduct;
            set => SetProperty(ref _isExistingProduct, value);
        }

        public decimal NewQuantity
        {
            get => _newQuantity;
            set => SetProperty(ref _newQuantity, value);
        }

        public decimal NewPurchasePrice
        {
            get => _newPurchasePrice;
            set => SetProperty(ref _newPurchasePrice, value);
        }

        // Commands
        public ICommand AddProductCommand { get; }
        public ICommand SaveProductCommand { get; }
        public ICommand DeleteProductCommand { get; }
        public ICommand TransferFromStorehouseCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand GenerateBarcodeCommand { get; }
        public ICommand ResetTransferCommand { get; }
        public ICommand SelectProductCommand { get; }
        public ICommand SelectMatchingProductCommand { get; }
        public ICommand ClearMatchingProductsCommand { get; }

        // Constructor
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
            _matchingProducts = new ObservableCollection<ProductDTO>();
            _validationErrors = new Dictionary<string, string>();
            _productChangedHandler = HandleProductChanged;

            // Initialize timers for debouncing
            _searchTimer = new Timer(OnSearchTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
            _barcodeTimer = new Timer(OnBarcodeTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            // Initialize commands
            AddProductCommand = new RelayCommand(_ => AddProduct());
            SaveProductCommand = new AsyncRelayCommand(async _ => await SaveProductAsync());
            DeleteProductCommand = new AsyncRelayCommand(async _ => await DeleteProductAsync());
            TransferFromStorehouseCommand = new AsyncRelayCommand(async _ => await TransferFromStorehouseAsync());
            RefreshCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            SearchCommand = new AsyncRelayCommand(async _ => await SearchProductsAsync());
            GenerateBarcodeCommand = new RelayCommand(GenerateBarcode);
            ResetTransferCommand = new RelayCommand(_ => ResetTransfer());
            SelectProductCommand = new RelayCommand(SelectProduct);
            SelectMatchingProductCommand = new RelayCommand(SelectMatchingProduct);
            ClearMatchingProductsCommand = new RelayCommand(_ => ClearMatchingProducts());

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

        // Event handlers
        private async void HandleProductChanged(EntityChangedEvent<ProductDTO> evt)
        {
            await LoadDataAsync();
        }

        private void SelectProduct(object parameter)
        {
            if (parameter is ProductDTO product)
            {
                // Cancel any ongoing operations first
                CancelSearchOperations();

                // Clear any existing product state
                ClearExistingProductState();

                // First, reset IsSelected for all products
                foreach (var p in Products)
                {
                    p.IsSelected = false;
                }

                // Set the selected product
                product.IsSelected = true;

                // Create a copy to avoid reference issues and mark as editing existing product
                var productCopy = CreateProductCopy(product);

                // Set flag to indicate this is editing an existing product (not creating new)
                _isEditingExistingProduct = true;

                // Don't subscribe to new product change events when editing existing
                SelectedProduct = productCopy;
            }
        }



        private async void OnSelectedProductPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProductDTO.SupplierId))
            {
                await LoadSupplierInvoicesAsync();
            }
        }
        private void OnNewProductPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Don't trigger events during auto-fill or when suppressed
            if (_isAutoFilling || _suppressPropertyChangeEvents || sender is not ProductDTO product)
                return;

            switch (e.PropertyName)
            {
                case nameof(ProductDTO.Name):
                    // Only trigger search if we're not in existing product mode
                    if (!IsExistingProduct)
                    {
                        DebouncedSearchByName(product.Name);
                    }
                    break;
                case nameof(ProductDTO.Barcode):
                    // Only trigger barcode validation if we're not in existing product mode
                    if (!IsExistingProduct)
                    {
                        DebouncedBarcodeValidation(product.Barcode);
                    }
                    break;
            }
        }
        // Core data loading
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

                var productsTask = SafeDatabaseOperation(() => _productService.GetAllAsync());
                var categoriesTask = SafeDatabaseOperation(() => _categoryService.GetProductCategoriesAsync());
                var suppliersTask = SafeDatabaseOperation(() => _supplierService.GetActiveAsync());

                await Task.WhenAll(productsTask, categoriesTask, suppliersTask);

                var products = await productsTask;
                var categories = await categoriesTask;
                var suppliers = await suppliersTask;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Products = new ObservableCollection<ProductDTO>(products ?? new List<ProductDTO>());
                    Categories = new ObservableCollection<CategoryDTO>(categories ?? new List<CategoryDTO>());
                    Suppliers = new ObservableCollection<SupplierDTO>(suppliers ?? new List<SupplierDTO>());

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

        private async Task LoadSupplierInvoicesAsync()
        {
            if (SelectedProduct?.SupplierId == null)
            {
                SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>();
                return;
            }

            try
            {
                var invoices = await SafeDatabaseOperation(() =>
                    _supplierInvoiceService.GetBySupplierAsync(SelectedProduct.SupplierId.Value));
                var draftInvoices = invoices?.Where(i => i.Status == "Draft").ToList() ?? new List<SupplierInvoiceDTO>();

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

        // Product management
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

            // Cancel any ongoing operations
            CancelSearchOperations();

            // Clear existing product state
            ClearExistingProductState();

            // Reset flags
            _isAutoFilling = false;
            _suppressPropertyChangeEvents = false;

            // Make sure we're not in editing existing product mode
            _isEditingExistingProduct = false;

            // Subscribe to property changes for real-time validation
            newProduct.PropertyChanged += OnNewProductPropertyChanged;

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

                var productBeingSaved = SelectedProduct;

                // Determine the operation type more accurately
                bool isExistingProductRestock = IsExistingProduct && _originalProduct != null;
                bool isRegularProductUpdate = !isExistingProductRestock && productBeingSaved.ProductId > 0;
                bool isNewProduct = !isExistingProductRestock && productBeingSaved.ProductId == 0;

                if (isExistingProductRestock)
                {
                    LoadingMessage = "Updating existing product with new stock...";

                    // Validate that we have the necessary data
                    if (NewQuantity <= 0)
                    {
                        ShowTemporaryErrorMessage("Please enter a valid quantity to add.");
                        return;
                    }

                    if (NewPurchasePrice <= 0)
                    {
                        ShowTemporaryErrorMessage("Please enter a valid purchase price for the new stock.");
                        return;
                    }

                    // Create a DTO for the existing product with updated values
                    var updatedProduct = new ProductDTO
                    {
                        ProductId = _originalProduct.ProductId, // Use the original product ID
                        Name = productBeingSaved.Name,
                        Barcode = productBeingSaved.Barcode,
                        Description = productBeingSaved.Description,
                        CategoryId = productBeingSaved.CategoryId,
                        SupplierId = productBeingSaved.SupplierId,
                        SalePrice = productBeingSaved.SalePrice,
                        WholesalePrice = productBeingSaved.WholesalePrice,
                        MinimumStock = productBeingSaved.MinimumStock,
                        IsActive = productBeingSaved.IsActive,
                        ImagePath = productBeingSaved.ImagePath,
                        BoxBarcode = productBeingSaved.BoxBarcode,
                        ItemsPerBox = productBeingSaved.ItemsPerBox,
                        BoxSalePrice = productBeingSaved.BoxSalePrice,
                        BoxWholesalePrice = productBeingSaved.BoxWholesalePrice,
                        MinimumBoxStock = productBeingSaved.MinimumBoxStock,
                        NumberOfBoxes = productBeingSaved.NumberOfBoxes,
                        UpdatedAt = DateTime.Now
                    };

                    // Get current totals from original product
                    var originalTotalQuantity = _originalProduct.CurrentStock + _originalProduct.Storehouse;
                    var newTotalQuantity = originalTotalQuantity + NewQuantity;

                    // Calculate average purchase price with rounding to 3 decimal places
                    decimal averagePurchasePrice = 0;
                    if (newTotalQuantity > 0)
                    {
                        var originalValue = originalTotalQuantity * _originalProduct.PurchasePrice;
                        var newValue = NewQuantity * NewPurchasePrice;
                        averagePurchasePrice = Math.Round((originalValue + newValue) / newTotalQuantity, 3);
                    }

                    // Update the product with merged values
                    updatedProduct.PurchasePrice = averagePurchasePrice;
                    updatedProduct.Storehouse = _originalProduct.Storehouse + NewQuantity;
                    updatedProduct.CurrentStock = _originalProduct.CurrentStock;

                    // Update box pricing if needed (also round to 3 decimal places)
                    if (updatedProduct.ItemsPerBox > 0)
                    {
                        if (updatedProduct.BoxPurchasePrice == 0 && updatedProduct.PurchasePrice > 0)
                            updatedProduct.BoxPurchasePrice = Math.Round(updatedProduct.PurchasePrice * updatedProduct.ItemsPerBox, 3);
                        else if (productBeingSaved.BoxPurchasePrice > 0)
                            updatedProduct.BoxPurchasePrice = Math.Round(productBeingSaved.BoxPurchasePrice, 3);
                    }

                    await SafeDatabaseOperation(() => _productService.UpdateAsync(updatedProduct));

                    if (SelectedSupplierInvoice != null)
                    {
                        await LinkProductToInvoiceAsync(updatedProduct);
                    }

                    await ShowSuccessMessage($"Product updated successfully. Added {NewQuantity} units. New average purchase price: {averagePurchasePrice:C}");

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        UpdateProductInCollection(updatedProduct);
                    });
                }
                else if (isRegularProductUpdate)
                {
                    LoadingMessage = "Updating product...";

                    productBeingSaved.UpdatedAt = DateTime.Now;

                    // Round all price fields to 3 decimal places
                    productBeingSaved.PurchasePrice = Math.Round(productBeingSaved.PurchasePrice, 3);
                    productBeingSaved.SalePrice = Math.Round(productBeingSaved.SalePrice, 3);
                    productBeingSaved.WholesalePrice = Math.Round(productBeingSaved.WholesalePrice, 3);

                    // Calculate box prices if not set (also round to 3 decimal places)
                    if (productBeingSaved.ItemsPerBox > 0)
                    {
                        if (productBeingSaved.BoxPurchasePrice == 0 && productBeingSaved.PurchasePrice > 0)
                            productBeingSaved.BoxPurchasePrice = Math.Round(productBeingSaved.PurchasePrice * productBeingSaved.ItemsPerBox, 3);
                        else
                            productBeingSaved.BoxPurchasePrice = Math.Round(productBeingSaved.BoxPurchasePrice, 3);

                        if (productBeingSaved.BoxSalePrice == 0 && productBeingSaved.SalePrice > 0)
                            productBeingSaved.BoxSalePrice = Math.Round(productBeingSaved.SalePrice * productBeingSaved.ItemsPerBox, 3);
                        else
                            productBeingSaved.BoxSalePrice = Math.Round(productBeingSaved.BoxSalePrice, 3);

                        if (productBeingSaved.BoxWholesalePrice == 0 && productBeingSaved.WholesalePrice > 0)
                            productBeingSaved.BoxWholesalePrice = Math.Round(productBeingSaved.WholesalePrice * productBeingSaved.ItemsPerBox, 3);
                        else
                            productBeingSaved.BoxWholesalePrice = Math.Round(productBeingSaved.BoxWholesalePrice, 3);
                    }

                    await SafeDatabaseOperation(() => _productService.UpdateAsync(productBeingSaved));

                    if (SelectedSupplierInvoice != null)
                    {
                        await LinkProductToInvoiceAsync(productBeingSaved);
                    }

                    await ShowSuccessMessage("Product updated successfully.");

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        UpdateProductInCollection(productBeingSaved);
                    });
                }
                else if (isNewProduct)
                {
                    LoadingMessage = "Creating new product...";

                    productBeingSaved.CreatedAt = DateTime.Now;

                    // Round all price fields to 3 decimal places
                    productBeingSaved.PurchasePrice = Math.Round(productBeingSaved.PurchasePrice, 3);
                    productBeingSaved.SalePrice = Math.Round(productBeingSaved.SalePrice, 3);
                    productBeingSaved.WholesalePrice = Math.Round(productBeingSaved.WholesalePrice, 3);

                    // Calculate box prices if not set (also round to 3 decimal places)
                    if (productBeingSaved.ItemsPerBox > 0)
                    {
                        if (productBeingSaved.BoxPurchasePrice == 0 && productBeingSaved.PurchasePrice > 0)
                            productBeingSaved.BoxPurchasePrice = Math.Round(productBeingSaved.PurchasePrice * productBeingSaved.ItemsPerBox, 3);
                        else
                            productBeingSaved.BoxPurchasePrice = Math.Round(productBeingSaved.BoxPurchasePrice, 3);

                        if (productBeingSaved.BoxSalePrice == 0 && productBeingSaved.SalePrice > 0)
                            productBeingSaved.BoxSalePrice = Math.Round(productBeingSaved.SalePrice * productBeingSaved.ItemsPerBox, 3);
                        else
                            productBeingSaved.BoxSalePrice = Math.Round(productBeingSaved.BoxSalePrice, 3);

                        if (productBeingSaved.BoxWholesalePrice == 0 && productBeingSaved.WholesalePrice > 0)
                            productBeingSaved.BoxWholesalePrice = Math.Round(productBeingSaved.WholesalePrice * productBeingSaved.ItemsPerBox, 3);
                        else
                            productBeingSaved.BoxWholesalePrice = Math.Round(productBeingSaved.BoxWholesalePrice, 3);
                    }

                    var savedProduct = await SafeDatabaseOperation(() => _productService.CreateAsync(productBeingSaved));

                    if (SelectedSupplierInvoice != null && savedProduct != null)
                    {
                        await LinkProductToInvoiceAsync(savedProduct);
                    }

                    await ShowSuccessMessage("Product created successfully.");

                    if (savedProduct != null)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Products.Add(savedProduct);
                        });
                    }
                }

                // Reset state
                _isEditingExistingProduct = false;
                ClearExistingProductState();
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
        private bool ValidateExistingProductRestock()
        {
            if (!IsExistingProduct || _originalProduct == null)
                return true; // Not an existing product restock, use regular validation

            var errors = new List<string>();

            if (NewQuantity <= 0)
                errors.Add("Please enter a valid quantity to add (must be greater than 0).");

            if (NewPurchasePrice <= 0)
                errors.Add("Please enter a valid purchase price for the new stock (must be greater than 0).");

            if (errors.Any())
            {
                ShowValidationErrors(errors);
                return false;
            }

            return true;
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

                    await SafeDatabaseOperation(() => _productService.DeleteAsync(productId));

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

        // Transfer operations
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

                var success = await SafeDatabaseOperation(() =>
                    _productService.TransferFromStorehouseAsync(SelectedProduct.ProductId, actualTransferQuantity));

                if (success == true)
                {
                    SelectedProduct.Storehouse -= actualTransferQuantity;
                    SelectedProduct.CurrentStock += actualTransferQuantity;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        UpdateProductInCollection(SelectedProduct);
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

        // Search operations
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

                var products = await SafeDatabaseOperation(() => _productService.SearchByNameAsync(SearchText));

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Products = new ObservableCollection<ProductDTO>(products ?? new List<ProductDTO>());
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

        // Debounced operations
        private void DebouncedSearchByName(string name)
        {
            lock (_searchLock)
            {
                // Cancel previous search
                _searchCancellationTokenSource?.Cancel();
                _searchCancellationTokenSource = new CancellationTokenSource();

                // Store the search term for the timer
                _pendingSearchTerm = name;

                // Reset timer - 500ms delay
                _searchTimer?.Change(500, Timeout.Infinite);
            }
        }

        private void DebouncedBarcodeValidation(string barcode)
        {
            lock (_barcodeLock)
            {
                // Cancel previous validation
                _barcodeCancellationTokenSource?.Cancel();
                _barcodeCancellationTokenSource = new CancellationTokenSource();

                // Store the barcode for the timer
                _pendingBarcode = barcode;

                // Reset timer - 300ms delay (shorter for barcode as it's usually scanned)
                _barcodeTimer?.Change(300, Timeout.Infinite);
            }
        }

        // Timer callbacks
        private async void OnSearchTimerElapsed(object? state)
        {
            string searchTerm;
            CancellationToken cancellationToken;

            lock (_searchLock)
            {
                searchTerm = _pendingSearchTerm;
                cancellationToken = _searchCancellationTokenSource?.Token ?? CancellationToken.None;
            }

            if (!string.IsNullOrWhiteSpace(searchTerm) && searchTerm.Length >= 3)
            {
                await SearchMatchingProductsByName(searchTerm, cancellationToken);
            }
            else
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(ClearMatchingProducts);
            }
        }

        private async void OnBarcodeTimerElapsed(object? state)
        {
            string barcode;
            CancellationToken cancellationToken;

            lock (_barcodeLock)
            {
                barcode = _pendingBarcode;
                cancellationToken = _barcodeCancellationTokenSource?.Token ?? CancellationToken.None;
            }

            if (!string.IsNullOrWhiteSpace(barcode))
            {
                await ValidateBarcodeAsync(barcode, cancellationToken);
            }
            else
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(ClearBarcodeValidation);
            }
        }

        // Product matching operations
        private async Task SearchMatchingProductsByName(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(ClearMatchingProducts);
                return;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                var products = await SafeDatabaseOperation(() => _productService.SearchByNameAsync(name));

                if (cancellationToken.IsCancellationRequested) return;

                var matchingProducts = products?.Where(p =>
                    p.Name.Contains(name, StringComparison.OrdinalIgnoreCase) &&
                    p.ProductId != (SelectedProduct?.ProductId ?? 0))
                    .Take(10)
                    .ToList() ?? new List<ProductDTO>();

                if (cancellationToken.IsCancellationRequested) return;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        MatchingProducts = new ObservableCollection<ProductDTO>(matchingProducts);
                        ShowMatchingProducts = matchingProducts.Any();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when operation is cancelled
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine($"Error searching products: {ex.Message}");
                }
            }
        }

        private async Task ValidateBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(barcode))
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(ClearBarcodeValidation);
                return;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                var existingProduct = await SafeDatabaseOperation(() => _productService.GetByBarcodeAsync(barcode));

                if (cancellationToken.IsCancellationRequested) return;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    if (existingProduct != null && existingProduct.ProductId != (SelectedProduct?.ProductId ?? 0))
                    {
                        // Only auto-fill if we're adding a new product (ProductId == 0) and not editing an existing one
                        if (SelectedProduct?.ProductId == 0 && !_isEditingExistingProduct)
                        {
                            SetBarcodeValidation($"Product with this barcode already exists: {existingProduct.Name}");
                            await AutoFillFromExistingProduct(existingProduct);
                        }
                        else if (_isEditingExistingProduct)
                        {
                            // If editing an existing product and barcode conflicts with another product
                            SetBarcodeValidation($"This barcode is already used by another product: {existingProduct.Name}");
                        }
                    }
                    else
                    {
                        ClearBarcodeValidation();
                        if (IsExistingProduct)
                        {
                            ResetToNewProduct();
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when operation is cancelled
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine($"Error validating barcode: {ex.Message}");
                }
            }
        }

        private void SelectMatchingProduct(object parameter)
        {
            if (parameter is ProductDTO matchingProduct)
            {
                _ = AutoFillFromExistingProduct(matchingProduct);
                ClearMatchingProducts();
            }
        }
        private async Task AutoFillFromExistingProduct(ProductDTO existingProduct)
        {
            if (SelectedProduct == null || _isEditingExistingProduct || _isAutoFilling)
                return;

            try
            {
                // Set flag to prevent recursive calls
                _isAutoFilling = true;

                // Cancel any ongoing search operations when auto-filling
                CancelSearchOperations();

                // Store original product and current values
                _originalProduct = existingProduct;
                var currentQuantity = SelectedProduct.CurrentStock + SelectedProduct.Storehouse;
                var currentPurchasePrice = SelectedProduct.PurchasePrice;

                // Store new values for later merging
                NewQuantity = currentQuantity;
                NewPurchasePrice = currentPurchasePrice;

                // Suppress all property change events during auto-fill
                _suppressPropertyChangeEvents = true;
                SelectedProduct.PropertyChanged -= OnNewProductPropertyChanged;

                try
                {
                    // Auto-fill all product details BUT KEEP ProductId as 0 to maintain "new product" state
                    SelectedProduct.ProductId = 0; // Keep as 0 - this is key!
                    SelectedProduct.Name = existingProduct.Name;
                    SelectedProduct.Barcode = existingProduct.Barcode;
                    SelectedProduct.Description = existingProduct.Description;
                    SelectedProduct.CategoryId = existingProduct.CategoryId;
                    SelectedProduct.CategoryName = existingProduct.CategoryName;
                    SelectedProduct.SupplierId = existingProduct.SupplierId;
                    SelectedProduct.SupplierName = existingProduct.SupplierName;
                    SelectedProduct.SalePrice = existingProduct.SalePrice;
                    SelectedProduct.WholesalePrice = existingProduct.WholesalePrice;
                    SelectedProduct.MinimumStock = existingProduct.MinimumStock;
                    SelectedProduct.IsActive = existingProduct.IsActive;
                    SelectedProduct.ImagePath = existingProduct.ImagePath;
                    SelectedProduct.BoxBarcode = existingProduct.BoxBarcode;
                    SelectedProduct.ItemsPerBox = existingProduct.ItemsPerBox;
                    SelectedProduct.BoxPurchasePrice = existingProduct.BoxPurchasePrice;
                    SelectedProduct.BoxSalePrice = existingProduct.BoxSalePrice;
                    SelectedProduct.BoxWholesalePrice = existingProduct.BoxWholesalePrice;
                    SelectedProduct.MinimumBoxStock = existingProduct.MinimumBoxStock;
                    SelectedProduct.NumberOfBoxes = existingProduct.NumberOfBoxes;

                    // Calculate current values for display
                    var totalExistingQuantity = existingProduct.CurrentStock + existingProduct.Storehouse;

                    // Set current stock values (don't modify yet - that happens on save)
                    SelectedProduct.CurrentStock = existingProduct.CurrentStock;
                    SelectedProduct.Storehouse = existingProduct.Storehouse;

                    // Calculate what the new average price would be if saved
                    decimal projectedAveragePrice = 0;
                    var newTotalQuantity = totalExistingQuantity + NewQuantity;
                    if (newTotalQuantity > 0)
                    {
                        var existingValue = totalExistingQuantity * existingProduct.PurchasePrice;
                        var newValue = NewQuantity * NewPurchasePrice;
                        projectedAveragePrice = Math.Round((existingValue + newValue) / newTotalQuantity, 3);
                    }

                    SelectedProduct.PurchasePrice = projectedAveragePrice;

                    IsExistingProduct = true;

                    SetBarcodeValidation($"Product found! Adding {NewQuantity} units. New average purchase price will be: {projectedAveragePrice:C}");
                }
                finally
                {
                    // Re-enable property change events but only for non-triggering properties
                    _suppressPropertyChangeEvents = false;
                    // Don't re-subscribe to OnNewProductPropertyChanged since we're now in existing product mode
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error auto-filling product: {ex.Message}");
                ClearBarcodeValidation();
                SetBarcodeValidation("Error loading product details. Please try again.");
            }
            finally
            {
                _isAutoFilling = false;
            }
        }


        // Utility methods
        private void ClearExistingProductState()
        {
            IsExistingProduct = false;
            _originalProduct = null;
            NewQuantity = 0;
            NewPurchasePrice = 0;
            _isAutoFilling = false;
            _suppressPropertyChangeEvents = false;
            ClearMatchingProducts();
            ClearBarcodeValidation();
            ClearTransferValidation();
        }

        private ProductDTO CreateProductCopy(ProductDTO original)
        {
            return new ProductDTO
            {
                ProductId = original.ProductId,
                Name = original.Name,
                Barcode = original.Barcode,
                Description = original.Description,
                CategoryId = original.CategoryId,
                CategoryName = original.CategoryName,
                SupplierId = original.SupplierId,
                SupplierName = original.SupplierName,
                PurchasePrice = original.PurchasePrice,
                SalePrice = original.SalePrice,
                WholesalePrice = original.WholesalePrice,
                CurrentStock = original.CurrentStock,
                Storehouse = original.Storehouse,
                MinimumStock = original.MinimumStock,
                IsActive = original.IsActive,
                ImagePath = original.ImagePath,
                BoxBarcode = original.BoxBarcode,
                ItemsPerBox = original.ItemsPerBox,
                BoxPurchasePrice = original.BoxPurchasePrice,
                BoxSalePrice = original.BoxSalePrice,
                BoxWholesalePrice = original.BoxWholesalePrice,
                NumberOfBoxes = original.NumberOfBoxes,
                MinimumBoxStock = original.MinimumBoxStock,
                CreatedAt = original.CreatedAt,
                UpdatedAt = original.UpdatedAt
            };
        }

        private void ResetToNewProduct()
        {
            if (SelectedProduct == null) return;

            SelectedProduct.ProductId = 0;
            IsExistingProduct = false;
            _originalProduct = null;
            NewQuantity = 0;
            NewPurchasePrice = 0;
        }

        private void ClearMatchingProducts()
        {
            MatchingProducts?.Clear();
            ShowMatchingProducts = false;
            SelectedMatchingProduct = null;
        }

        private void ClearBarcodeValidation()
        {
            BarcodeValidationMessage = string.Empty;
            HasBarcodeValidation = false;
        }

        private void SetBarcodeValidation(string message)
        {
            BarcodeValidationMessage = message;
            HasBarcodeValidation = !string.IsNullOrEmpty(message);
        }

        private void CancelSearchOperations()
        {
            lock (_searchLock)
            {
                _searchCancellationTokenSource?.Cancel();
                _searchCancellationTokenSource?.Dispose();
                _searchCancellationTokenSource = null;
                _searchTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _pendingSearchTerm = string.Empty;
            }

            lock (_barcodeLock)
            {
                _barcodeCancellationTokenSource?.Cancel();
                _barcodeCancellationTokenSource?.Dispose();
                _barcodeCancellationTokenSource = null;
                _barcodeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _pendingBarcode = string.Empty;
            }
        }

        private void UpdateProductInCollection(ProductDTO updatedProduct)
        {
            for (int i = 0; i < Products.Count; i++)
            {
                if (Products[i].ProductId == updatedProduct.ProductId)
                {
                    Products[i] = updatedProduct;
                    break;
                }
            }
        }

        // Safe database operation wrapper
        private async Task<T?> SafeDatabaseOperation<T>(Func<Task<T>> operation)
        {
            const int maxRetries = 3;
            const int baseDelayMs = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    lock (_databaseLock)
                    {
                        // Ensure only one database operation at a time
                    }

                    return await operation();
                }
                catch (InvalidOperationException ex) when (attempt < maxRetries)
                {
                    System.Diagnostics.Debug.WriteLine($"Database operation failed (attempt {attempt}): {ex.Message}");
                    await Task.Delay(baseDelayMs * attempt); // Exponential backoff
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Database operation error: {ex.Message}");
                    if (attempt == maxRetries)
                        throw;
                    await Task.Delay(baseDelayMs * attempt);
                }
            }

            return default(T);
        }

        private async Task SafeDatabaseOperation(Func<Task> operation)
        {
            const int maxRetries = 3;
            const int baseDelayMs = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    lock (_databaseLock)
                    {
                        // Ensure only one database operation at a time
                    }

                    await operation();
                    return;
                }
                catch (InvalidOperationException ex) when (attempt < maxRetries)
                {
                    System.Diagnostics.Debug.WriteLine($"Database operation failed (attempt {attempt}): {ex.Message}");
                    await Task.Delay(baseDelayMs * attempt); // Exponential backoff
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Database operation error: {ex.Message}");
                    if (attempt == maxRetries)
                        throw;
                    await Task.Delay(baseDelayMs * attempt);
                }
            }
        }

        // Other utility methods
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

        private async Task LinkProductToInvoiceAsync(ProductDTO product)
        {
            if (SelectedSupplierInvoice == null) return;

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

                await SafeDatabaseOperation(() => _supplierInvoiceService.AddProductToInvoiceAsync(invoiceDetail));
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Product saved but could not link to invoice: {ex.Message}");
            }
        }

        private bool ValidateProduct(ProductDTO? product)
        {
            ValidationErrors.Clear();

            if (product == null)
            {
                ValidationErrors.Add("General", "No product selected.");
                return false;
            }

            // For existing product restock, use different validation
            if (IsExistingProduct)
            {
                return ValidateExistingProductRestock();
            }

            // Regular validation for new products
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
                // Cancel and dispose timers
                CancelSearchOperations();
                _searchTimer?.Dispose();
                _barcodeTimer?.Dispose();

                if (_selectedProduct != null)
                {
                    _selectedProduct.PropertyChanged -= OnSelectedProductPropertyChanged;
                    _selectedProduct.PropertyChanged -= OnNewProductPropertyChanged;
                }

                ClearMatchingProducts();
                _operationLock?.Dispose();
                UnsubscribeFromEvents();
                _isDisposed = true;
            }

            base.Dispose();
        }
    }
}