using Microsoft.Win32;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Enums;
using QuickTechSystems.WPF.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

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

        private ProductFilterDTO _currentFilter;
        private PagedResultDTO<ProductDTO> _pagedResult;
        private ProductStatisticsDTO _statistics;
        private string _searchText = string.Empty;
        private int? _selectedCategoryId;
        private int? _selectedSupplierId;
        private StockStatus _selectedStockStatus = StockStatus.All;
        private bool? _selectedActiveStatus;
        private decimal? _minPrice;
        private decimal? _maxPrice;
        private decimal? _minStock;
        private decimal? _maxStock;
        private SortOption _selectedSortOption = SortOption.Name;
        private bool _sortDescending = false;
        private int _currentPage = 1;
        private int _pageSize = 25;
        private List<int> _pageSizeOptions = new List<int> { 10, 25, 50, 100 };

        private decimal _transferQuantity;
        private int _transferBoxes;
        private bool _isIndividualTransfer = true;
        private bool _isBoxTransfer;
        private string _transferValidationMessage = string.Empty;
        private bool _hasTransferValidationMessage;

        private ObservableCollection<ProductDTO> _matchingProducts;
        private ProductDTO? _selectedMatchingProduct;
        private bool _showMatchingProducts;
        private string _barcodeValidationMessage = string.Empty;
        private bool _hasBarcodeValidation;
        private bool _isExistingProduct;
        private ProductDTO? _originalProduct;
        private decimal _newQuantity;
        private decimal _newPurchasePrice;

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

        public PagedResultDTO<ProductDTO> PagedResult
        {
            get => _pagedResult;
            set => SetProperty(ref _pagedResult, value);
        }

        public ProductStatisticsDTO Statistics
        {
            get => _statistics;
            set => SetProperty(ref _statistics, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public int? SelectedCategoryId
        {
            get => _selectedCategoryId;
            set
            {
                if (SetProperty(ref _selectedCategoryId, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public int? SelectedSupplierId
        {
            get => _selectedSupplierId;
            set
            {
                if (SetProperty(ref _selectedSupplierId, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public StockStatus SelectedStockStatus
        {
            get => _selectedStockStatus;
            set
            {
                if (SetProperty(ref _selectedStockStatus, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public bool? SelectedActiveStatus
        {
            get => _selectedActiveStatus;
            set
            {
                if (SetProperty(ref _selectedActiveStatus, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public decimal? MinPrice
        {
            get => _minPrice;
            set
            {
                if (SetProperty(ref _minPrice, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public decimal? MaxPrice
        {
            get => _maxPrice;
            set
            {
                if (SetProperty(ref _maxPrice, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public decimal? MinStock
        {
            get => _minStock;
            set
            {
                if (SetProperty(ref _minStock, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public decimal? MaxStock
        {
            get => _maxStock;
            set
            {
                if (SetProperty(ref _maxStock, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public SortOption SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                if (SetProperty(ref _selectedSortOption, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public bool SortDescending
        {
            get => _sortDescending;
            set
            {
                if (SetProperty(ref _sortDescending, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    _ = LoadProductsAsync();
                }
            }
        }

        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    CurrentPage = 1;
                    _ = LoadProductsAsync();
                }
            }
        }

        public List<int> PageSizeOptions
        {
            get => _pageSizeOptions;
            set => SetProperty(ref _pageSizeOptions, value);
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

        public ICommand AddProductCommand { get; private set; }
        public ICommand SaveProductCommand { get; private set; }
        public ICommand DeleteProductCommand { get; private set; }
        public ICommand TransferFromStorehouseCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand SearchCommand { get; private set; }
        public ICommand GenerateBarcodeCommand { get; private set; }
        public ICommand ResetTransferCommand { get; private set; }
        public ICommand SelectProductCommand { get; private set; }
        public ICommand SelectMatchingProductCommand { get; private set; }
        public ICommand ClearMatchingProductsCommand { get; private set; }
        public ICommand ClearFiltersCommand { get; private set; }
        public ICommand FirstPageCommand { get; private set; }
        public ICommand PreviousPageCommand { get; private set; }
        public ICommand NextPageCommand { get; private set; }
        public ICommand LastPageCommand { get; private set; }
        public ICommand ExportToCsvCommand { get; private set; }
        public ICommand ExportToExcelCommand { get; private set; }
        public ICommand GoToPageCommand { get; private set; }

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
            _currentFilter = new ProductFilterDTO();
            _pagedResult = new PagedResultDTO<ProductDTO>();
            _statistics = new ProductStatisticsDTO();

            _searchTimer = new Timer(OnSearchTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
            _barcodeTimer = new Timer(OnBarcodeTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            InitializeCommands();
            _ = LoadDataAsync();
        }

        private void InitializeCommands()
        {
            AddProductCommand = new RelayCommand(_ => AddProduct());
            SaveProductCommand = new AsyncRelayCommand(async _ => await SaveProductAsync());
            DeleteProductCommand = new AsyncRelayCommand(async _ => await DeleteProductAsync());
            TransferFromStorehouseCommand = new AsyncRelayCommand(async _ => await TransferFromStorehouseAsync());
            RefreshCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            SearchCommand = new AsyncRelayCommand(async _ => await LoadProductsAsync());
            GenerateBarcodeCommand = new RelayCommand(GenerateBarcode);
            ResetTransferCommand = new RelayCommand(_ => ResetTransfer());
            SelectProductCommand = new RelayCommand(SelectProduct);
            SelectMatchingProductCommand = new RelayCommand(SelectMatchingProduct);
            ClearMatchingProductsCommand = new RelayCommand(_ => ClearMatchingProducts());
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
            FirstPageCommand = new RelayCommand(_ => FirstPage(), _ => CanGoToFirstPage());
            PreviousPageCommand = new RelayCommand(_ => PreviousPage(), _ => CanGoToPreviousPage());
            NextPageCommand = new RelayCommand(_ => NextPage(), _ => CanGoToNextPage());
            LastPageCommand = new RelayCommand(_ => LastPage(), _ => CanGoToLastPage());
            ExportToCsvCommand = new AsyncRelayCommand(async _ => await ExportToCsvAsync());
            ExportToExcelCommand = new AsyncRelayCommand(async _ => await ExportToExcelAsync());
            GoToPageCommand = new RelayCommand(GoToPage);
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

        protected override async Task LoadDataAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                IsLoading = true;
                LoadingMessage = "Loading data...";

                var categoriesTask = SafeDatabaseOperation(() => _categoryService.GetProductCategoriesAsync());
                var suppliersTask = SafeDatabaseOperation(() => _supplierService.GetActiveAsync());
                var statisticsTask = SafeDatabaseOperation(() => _productService.GetProductStatisticsAsync());

                await Task.WhenAll(categoriesTask, suppliersTask, statisticsTask);

                var categories = await categoriesTask;
                var suppliers = await suppliersTask;
                var statistics = await statisticsTask;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Categories = new ObservableCollection<CategoryDTO>(categories ?? new List<CategoryDTO>());
                    Suppliers = new ObservableCollection<SupplierDTO>(suppliers ?? new List<SupplierDTO>());
                    Statistics = statistics ?? new ProductStatisticsDTO();
                });

                await LoadProductsAsync();
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

        private async Task LoadProductsAsync()
        {
            try
            {
                IsLoading = true;
                LoadingMessage = "Loading products...";

                var filter = BuildCurrentFilter();
                var result = await SafeDatabaseOperation(() => _productService.GetPagedProductsAsync(filter));

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PagedResult = result ?? new PagedResultDTO<ProductDTO>();
                    Products = new ObservableCollection<ProductDTO>(PagedResult.Items);

                    OnPropertyChanged(nameof(Products));
                    OnPropertyChanged(nameof(PagedResult));
                });
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error loading products: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private ProductFilterDTO BuildCurrentFilter()
        {
            return new ProductFilterDTO
            {
                SearchTerm = SearchText,
                CategoryId = SelectedCategoryId,
                SupplierId = SelectedSupplierId,
                StockStatus = SelectedStockStatus,
                IsActive = SelectedActiveStatus,
                MinPrice = MinPrice,
                MaxPrice = MaxPrice,
                MinStock = MinStock,
                MaxStock = MaxStock,
                SortBy = SelectedSortOption,
                SortDescending = SortDescending,
                PageNumber = CurrentPage,
                PageSize = PageSize
            };
        }

        private void ClearFilters()
        {
            SearchText = string.Empty;
            SelectedCategoryId = null;
            SelectedSupplierId = null;
            SelectedStockStatus = StockStatus.All;
            SelectedActiveStatus = null;
            MinPrice = null;
            MaxPrice = null;
            MinStock = null;
            MaxStock = null;
            SelectedSortOption = SortOption.Name;
            SortDescending = false;
            CurrentPage = 1;
        }

        private void FirstPage()
        {
            CurrentPage = 1;
        }

        private bool CanGoToFirstPage()
        {
            return PagedResult?.HasPrevious == true;
        }

        private void PreviousPage()
        {
            if (CurrentPage > 1)
                CurrentPage--;
        }

        private bool CanGoToPreviousPage()
        {
            return PagedResult?.HasPrevious == true;
        }

        private void NextPage()
        {
            if (CurrentPage < PagedResult?.TotalPages)
                CurrentPage++;
        }

        private bool CanGoToNextPage()
        {
            return PagedResult?.HasNext == true;
        }

        private void LastPage()
        {
            CurrentPage = PagedResult?.TotalPages ?? 1;
        }

        private bool CanGoToLastPage()
        {
            return PagedResult?.HasNext == true;
        }

        private void GoToPage(object parameter)
        {
            if (parameter is int pageNumber && pageNumber > 0 && pageNumber <= (PagedResult?.TotalPages ?? 1))
            {
                CurrentPage = pageNumber;
            }
        }

        private async Task ExportToCsvAsync()
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = $"Products_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    IsLoading = true;
                    LoadingMessage = "Exporting to CSV...";

                    var filter = BuildCurrentFilter();
                    filter.PageNumber = 1;
                    filter.PageSize = int.MaxValue;

                    var data = await SafeDatabaseOperation(() => _productService.ExportProductsToCsvAsync(filter));

                    if (data != null)
                    {
                        await File.WriteAllBytesAsync(saveFileDialog.FileName, data);
                        await ShowSuccessMessage($"Data exported successfully to {saveFileDialog.FileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error exporting data: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExportToExcelAsync()
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel files (*.xlsx)|*.xlsx",
                    FileName = $"Products_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.xlsx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    IsLoading = true;
                    LoadingMessage = "Exporting to Excel...";

                    var filter = BuildCurrentFilter();
                    filter.PageNumber = 1;
                    filter.PageSize = int.MaxValue;

                    var data = await SafeDatabaseOperation(() => _productService.ExportProductsToExcelAsync(filter));

                    if (data != null)
                    {
                        await File.WriteAllBytesAsync(saveFileDialog.FileName, data);
                        await ShowSuccessMessage($"Data exported successfully to {saveFileDialog.FileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error exporting data: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
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

            CancelSearchOperations();
            ClearExistingProductState();
            _isAutoFilling = false;
            _suppressPropertyChangeEvents = false;
            _isEditingExistingProduct = false;
            newProduct.PropertyChanged += OnNewProductPropertyChanged;

            SelectedProduct = newProduct;
            SelectedSupplierInvoice = null;
        }

        private void SelectProduct(object parameter)
        {
            if (parameter is ProductDTO product)
            {
                CancelSearchOperations();
                ClearExistingProductState();

                foreach (var p in Products)
                {
                    p.IsSelected = false;
                }

                product.IsSelected = true;
                var productCopy = CreateProductCopy(product);
                _isEditingExistingProduct = true;
                SelectedProduct = productCopy;
            }
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

                bool isExistingProductRestock = IsExistingProduct && _originalProduct != null;
                bool isRegularProductUpdate = !isExistingProductRestock && productBeingSaved.ProductId > 0;
                bool isNewProduct = !isExistingProductRestock && productBeingSaved.ProductId == 0;

                if (isExistingProductRestock)
                {
                    LoadingMessage = "Updating existing product with new stock...";

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

                    var updatedProduct = new ProductDTO
                    {
                        ProductId = _originalProduct.ProductId,
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

                    var originalTotalQuantity = _originalProduct.CurrentStock + _originalProduct.Storehouse;
                    var newTotalQuantity = originalTotalQuantity + NewQuantity;

                    decimal averagePurchasePrice = 0;
                    if (newTotalQuantity > 0)
                    {
                        var originalValue = originalTotalQuantity * _originalProduct.PurchasePrice;
                        var newValue = NewQuantity * NewPurchasePrice;
                        averagePurchasePrice = Math.Round((originalValue + newValue) / newTotalQuantity, 3);
                    }

                    updatedProduct.PurchasePrice = averagePurchasePrice;
                    updatedProduct.Storehouse = _originalProduct.Storehouse + NewQuantity;
                    updatedProduct.CurrentStock = _originalProduct.CurrentStock;

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
                }
                else if (isRegularProductUpdate)
                {
                    LoadingMessage = "Updating product...";

                    productBeingSaved.UpdatedAt = DateTime.Now;
                    productBeingSaved.PurchasePrice = Math.Round(productBeingSaved.PurchasePrice, 3);
                    productBeingSaved.SalePrice = Math.Round(productBeingSaved.SalePrice, 3);
                    productBeingSaved.WholesalePrice = Math.Round(productBeingSaved.WholesalePrice, 3);

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
                }
                else if (isNewProduct)
                {
                    LoadingMessage = "Creating new product...";

                    productBeingSaved.CreatedAt = DateTime.Now;
                    productBeingSaved.PurchasePrice = Math.Round(productBeingSaved.PurchasePrice, 3);
                    productBeingSaved.SalePrice = Math.Round(productBeingSaved.SalePrice, 3);
                    productBeingSaved.WholesalePrice = Math.Round(productBeingSaved.WholesalePrice, 3);

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
                }

                _isEditingExistingProduct = false;
                ClearExistingProductState();
                SelectedProduct = null;
                SelectedSupplierInvoice = null;
                await LoadDataAsync();
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

                    SelectedProduct = null;
                    await ShowSuccessMessage("Product deleted successfully.");
                    await LoadDataAsync();
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

                var success = await SafeDatabaseOperation(() =>
                    _productService.TransferFromStorehouseAsync(SelectedProduct.ProductId, actualTransferQuantity));

                if (success == true)
                {
                    SelectedProduct.Storehouse -= actualTransferQuantity;
                    SelectedProduct.CurrentStock += actualTransferQuantity;

                    OnPropertyChanged(nameof(AvailableBoxes));
                    ResetTransferValues();
                    await ShowSuccessMessage($"Transfer completed successfully. {transferType}");
                    await LoadDataAsync();
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

        private async void OnSelectedProductPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProductDTO.SupplierId))
            {
                await LoadSupplierInvoicesAsync();
            }
        }

        private void OnNewProductPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isAutoFilling || _suppressPropertyChangeEvents || sender is not ProductDTO product)
                return;

            switch (e.PropertyName)
            {
                case nameof(ProductDTO.Name):
                    if (!IsExistingProduct)
                    {
                        DebouncedSearchByName(product.Name);
                    }
                    break;
                case nameof(ProductDTO.Barcode):
                    if (!IsExistingProduct)
                    {
                        DebouncedBarcodeValidation(product.Barcode);
                    }
                    break;
            }
        }

        private void DebouncedSearchByName(string name)
        {
            lock (_searchLock)
            {
                _searchCancellationTokenSource?.Cancel();
                _searchCancellationTokenSource = new CancellationTokenSource();
                _pendingSearchTerm = name;
                _searchTimer?.Change(500, Timeout.Infinite);
            }
        }

        private void DebouncedBarcodeValidation(string barcode)
        {
            lock (_barcodeLock)
            {
                _barcodeCancellationTokenSource?.Cancel();
                _barcodeCancellationTokenSource = new CancellationTokenSource();
                _pendingBarcode = barcode;
                _barcodeTimer?.Change(300, Timeout.Infinite);
            }
        }

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
                        if (SelectedProduct?.ProductId == 0 && !_isEditingExistingProduct)
                        {
                            SetBarcodeValidation($"Product with this barcode already exists: {existingProduct.Name}");
                            await AutoFillFromExistingProduct(existingProduct);
                        }
                        else if (_isEditingExistingProduct)
                        {
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
                _isAutoFilling = true;
                CancelSearchOperations();

                _originalProduct = existingProduct;
                var currentQuantity = SelectedProduct.CurrentStock + SelectedProduct.Storehouse;
                var currentPurchasePrice = SelectedProduct.PurchasePrice;

                NewQuantity = currentQuantity;
                NewPurchasePrice = currentPurchasePrice;

                _suppressPropertyChangeEvents = true;
                SelectedProduct.PropertyChanged -= OnNewProductPropertyChanged;

                try
                {
                    SelectedProduct.ProductId = 0;
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

                    var totalExistingQuantity = existingProduct.CurrentStock + existingProduct.Storehouse;
                    SelectedProduct.CurrentStock = existingProduct.CurrentStock;
                    SelectedProduct.Storehouse = existingProduct.Storehouse;

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
                    _suppressPropertyChangeEvents = false;
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
                    }

                    return await operation();
                }
                catch (InvalidOperationException ex) when (attempt < maxRetries)
                {
                    System.Diagnostics.Debug.WriteLine($"Database operation failed (attempt {attempt}): {ex.Message}");
                    await Task.Delay(baseDelayMs * attempt);
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
                    }

                    await operation();
                    return;
                }
                catch (InvalidOperationException ex) when (attempt < maxRetries)
                {
                    System.Diagnostics.Debug.WriteLine($"Database operation failed (attempt {attempt}): {ex.Message}");
                    await Task.Delay(baseDelayMs * attempt);
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

        private void GenerateBarcode(object parameter)
        {
            if (SelectedProduct != null)
            {
                var random = new Random();
                var length = random.Next(4, 11);
                var minValue = (long)Math.Pow(10, length - 1);
                var maxValue = (long)Math.Pow(10, length) - 1;
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

            if (IsExistingProduct)
            {
                return ValidateExistingProductRestock();
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

        private bool ValidateExistingProductRestock()
        {
            if (!IsExistingProduct || _originalProduct == null)
                return true;

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