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
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;

        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<CategoryDTO> _categories;
        private ProductDTO? _selectedProduct;
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

        private readonly object _databaseLock = new object();

        public IEnumerable<StockStatus> StockStatusOptions =>
            Enum.GetValues<StockStatus>();

        public IEnumerable<SortOption> SortOptionOptions =>
            Enum.GetValues<SortOption>();

        public bool HasSelectedProduct => SelectedProduct != null;

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
                    OnPropertyChanged(nameof(HasSelectedProduct));

                    if (value != null)
                    {
                        value.PropertyChanged += OnSelectedProductPropertyChanged;
                    }
                }
            }
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

        public ICommand SaveProductCommand { get; private set; }
        public ICommand DeleteProductCommand { get; private set; }
        public ICommand TransferFromStorehouseCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand SearchCommand { get; private set; }
        public ICommand GenerateBarcodeCommand { get; private set; }
        public ICommand ResetTransferCommand { get; private set; }
        public ICommand SelectProductCommand { get; private set; }
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
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _productService = productService;
            _categoryService = categoryService;
            _products = new ObservableCollection<ProductDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _validationErrors = new Dictionary<string, string>();
            _productChangedHandler = HandleProductChanged;
            _currentFilter = new ProductFilterDTO();
            _pagedResult = new PagedResultDTO<ProductDTO>();
            _statistics = new ProductStatisticsDTO();

            InitializeCommands();
            _ = LoadDataAsync();
        }

        private void InitializeCommands()
        {
            SaveProductCommand = new AsyncRelayCommand(async _ => await SaveProductAsync());
            DeleteProductCommand = new AsyncRelayCommand(async _ => await DeleteProductAsync());
            TransferFromStorehouseCommand = new AsyncRelayCommand(async _ => await TransferFromStorehouseAsync());
            RefreshCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            SearchCommand = new AsyncRelayCommand(async _ => await LoadProductsAsync());
            GenerateBarcodeCommand = new RelayCommand(GenerateBarcode);
            ResetTransferCommand = new RelayCommand(_ => ResetTransfer());
            SelectProductCommand = new RelayCommand(SelectProduct);
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
                var statisticsTask = SafeDatabaseOperation(() => _productService.GetProductStatisticsAsync());

                await Task.WhenAll(categoriesTask, statisticsTask);

                var categories = await categoriesTask;
                var statistics = await statisticsTask;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Categories = new ObservableCollection<CategoryDTO>(categories ?? new List<CategoryDTO>());
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

        private void SelectProduct(object parameter)
        {
            System.Diagnostics.Debug.WriteLine($"SelectProduct called with parameter: {parameter?.GetType().Name}");

            if (parameter is ProductDTO product)
            {
                System.Diagnostics.Debug.WriteLine($"Selecting product: {product.Name} (ID: {product.ProductId})");

                foreach (var p in Products)
                {
                    p.IsSelected = false;
                }

                product.IsSelected = true;
                var productCopy = CreateProductCopy(product);

                System.Diagnostics.Debug.WriteLine($"Created product copy: {productCopy.Name} (ID: {productCopy.ProductId})");

                SelectedProduct = productCopy;

                System.Diagnostics.Debug.WriteLine($"SelectedProduct set. HasSelectedProduct: {HasSelectedProduct}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Parameter is not ProductDTO. Type: {parameter?.GetType().Name ?? "null"}");
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

                if (SelectedProduct == null || SelectedProduct.ProductId == 0)
                {
                    ShowTemporaryErrorMessage("No product selected for editing.");
                    return;
                }

                IsLoading = true;
                LoadingMessage = "Updating product...";

                var productBeingSaved = SelectedProduct;

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

                await ShowSuccessMessage("Product updated successfully.");
                SelectedProduct = null;
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

        private void OnSelectedProductPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // No specific handling needed for property changes in edit-only mode
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

        private bool ValidateProduct(ProductDTO? product)
        {
            ValidationErrors.Clear();

            if (product == null)
            {
                ValidationErrors.Add("General", "No product selected.");
                return false;
            }

            if (product.ProductId == 0)
            {
                ValidationErrors.Add("General", "Cannot save product without a valid ID. Please select an existing product to edit.");
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