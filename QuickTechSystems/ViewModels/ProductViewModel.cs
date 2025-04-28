// Path: QuickTechSystems.WPF.ViewModels/ProductViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Threading;
using System.Text;

namespace QuickTechSystems.WPF.ViewModels
{
    public class ProductViewModel : ViewModelBase
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IMainStockService _mainStockService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;
        private FlowDirection _flowDirection = FlowDirection.LeftToRight;
        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<ProductDTO> _filteredProducts;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<MainStockDTO> _mainStockItems;
        private ProductDTO? _selectedProduct;
        private string _searchText = string.Empty;
        private bool _isSaving;
        private string _statusMessage = string.Empty;
        private int _stockIncrement;
        private Dictionary<int, List<string>> _validationErrors;
        private Action<EntityChangedEvent<ProductDTO>> _productChangedHandler;
        private readonly Action<EntityChangedEvent<CategoryDTO>> _categoryChangedHandler;
        private readonly Action<EntityChangedEvent<MainStockDTO>> _mainStockChangedHandler;
        private int _labelsPerProduct = 1;
        private decimal _totalProfit;
        private CancellationTokenSource _cts;
        private readonly Action<ProductStockUpdatedEvent> _productStockUpdatedHandler;

        private readonly ISupplierService _supplierService;
        private ObservableCollection<SupplierDTO> _suppliers;
        // Pagination properties
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalPages;
        private ObservableCollection<int> _pageNumbers;
        private List<int> _visiblePageNumbers = new List<int>();
        private int _totalProducts;

        public FlowDirection FlowDirection
        {
            get => _flowDirection;
            set => SetProperty(ref _flowDirection, value);
        }

        public decimal TotalProfit
        {
            get => _totalProfit;
            set => SetProperty(ref _totalProfit, value);
        }

        // New properties for aggregated values
        private decimal _totalPurchaseValue;
        private decimal _totalSaleValue;
        private decimal _selectedProductTotalCost;
        private decimal _selectedProductTotalValue;
        private decimal _selectedProductProfitMargin;
        private decimal _selectedProductProfitPercentage;

        public decimal TotalPurchaseValue
        {
            get => _totalPurchaseValue;
            set => SetProperty(ref _totalPurchaseValue, value);
        }

        public decimal TotalSaleValue
        {
            get => _totalSaleValue;
            set => SetProperty(ref _totalSaleValue, value);
        }

        public decimal SelectedProductTotalCost
        {
            get => _selectedProductTotalCost;
            set => SetProperty(ref _selectedProductTotalCost, value);
        }

        public decimal SelectedProductTotalValue
        {
            get => _selectedProductTotalValue;
            set => SetProperty(ref _selectedProductTotalValue, value);
        }
        public ObservableCollection<SupplierDTO> Suppliers
        {
            get => _suppliers;
            set => SetProperty(ref _suppliers, value);
        }
        public decimal SelectedProductProfitMargin
        {
            get => _selectedProductProfitMargin;
            set => SetProperty(ref _selectedProductProfitMargin, value);
        }

        public decimal SelectedProductProfitPercentage
        {
            get => _selectedProductProfitPercentage;
            set => SetProperty(ref _selectedProductProfitPercentage, value);
        }

        public int LabelsPerProduct
        {
            get => _labelsPerProduct;
            set => SetProperty(ref _labelsPerProduct, Math.Max(1, value));
        }

        public ObservableCollection<ProductDTO> Products
        {
            get => _products;
            set => SetProperty(ref _products, value);
        }

        public ObservableCollection<ProductDTO> FilteredProducts
        {
            get => _filteredProducts;
            set => SetProperty(ref _filteredProducts, value);
        }

        public ObservableCollection<CategoryDTO> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public ObservableCollection<MainStockDTO> MainStockItems
        {
            get => _mainStockItems;
            set => SetProperty(ref _mainStockItems, value);
        }

        public ProductDTO? SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (_selectedProduct != null)
                {
                    // Unsubscribe from property changes on the old selected product
                    _selectedProduct.PropertyChanged -= SelectedProduct_PropertyChanged;
                }

                SetProperty(ref _selectedProduct, value);

                if (value != null)
                {
                    // Subscribe to property changes on the new selected product
                    value.PropertyChanged += SelectedProduct_PropertyChanged;

                    // Update calculations when selecting a product
                    CalculateSelectedProductValues();
                }
                else
                {
                    SelectedProductTotalCost = 0;
                    SelectedProductTotalValue = 0;
                    SelectedProductProfitMargin = 0;
                    SelectedProductProfitPercentage = 0;
                }
            }
        }

     
        

     

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _currentPage = 1; // Reset to first page when searching
                    OnPropertyChanged(nameof(CurrentPage));
                    FilterProducts();
                }
            }
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

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public int StockIncrement
        {
            get => _stockIncrement;
            set => SetProperty(ref _stockIncrement, value);
        }

        public Dictionary<int, List<string>> ValidationErrors
        {
            get => _validationErrors;
            set => SetProperty(ref _validationErrors, value);
        }

        // Pagination properties
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (value < 1 || value > TotalPages) return;
                if (SetProperty(ref _currentPage, value))
                {
                    _ = SafeLoadDataAsync();
                    UpdateVisiblePageNumbers();
                    OnPropertyChanged(nameof(IsFirstPage));
                    OnPropertyChanged(nameof(IsLastPage));
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
                    _currentPage = 1; // Reset to first page when changing page size
                    OnPropertyChanged(nameof(CurrentPage));
                    _ = SafeLoadDataAsync();
                }
            }
        }

        public int TotalPages
        {
            get => _totalPages;
            private set
            {
                if (SetProperty(ref _totalPages, value))
                {
                    UpdateVisiblePageNumbers();
                    OnPropertyChanged(nameof(IsFirstPage));
                    OnPropertyChanged(nameof(IsLastPage));
                }
            }
        }

        public int TotalProducts
        {
            get => _totalProducts;
            private set => SetProperty(ref _totalProducts, value);
        }

        public ObservableCollection<int> PageNumbers
        {
            get => _pageNumbers;
            private set => SetProperty(ref _pageNumbers, value);
        }

        public List<int> VisiblePageNumbers
        {
            get => _visiblePageNumbers;
            private set => SetProperty(ref _visiblePageNumbers, value);
        }

        public bool IsFirstPage => CurrentPage <= 1;
        public bool IsLastPage => CurrentPage >= TotalPages;

        public ObservableCollection<int> AvailablePageSizes { get; } = new ObservableCollection<int> { 10, 25, 50, 100 };

        public ICommand LoadCommand { get; private set; }
        public ICommand UpdateStockCommand { get; private set; }
        public ICommand PrintBarcodeCommand { get; private set; }
       
        // Pagination commands
        public ICommand NextPageCommand { get; private set; }
        public ICommand PreviousPageCommand { get; private set; }
        public ICommand GoToPageCommand { get; private set; }
        public ICommand ChangePageSizeCommand { get; private set; }
         public ProductViewModel(
            IProductService productService,

    ISupplierService supplierService,
            ICategoryService categoryService,
            IMainStockService mainStockService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            Debug.WriteLine("Initializing ProductViewModel");
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _mainStockService = mainStockService ?? throw new ArgumentNullException(nameof(mainStockService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));

            _productStockUpdatedHandler = HandleProductStockUpdated;
            _suppliers = new ObservableCollection<SupplierDTO>();
            _products = new ObservableCollection<ProductDTO>();
            _filteredProducts = new ObservableCollection<ProductDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _mainStockItems = new ObservableCollection<MainStockDTO>();
            _validationErrors = new Dictionary<int, List<string>>();
            _productChangedHandler = HandleProductChanged;
            _categoryChangedHandler = HandleCategoryChanged;
            _mainStockChangedHandler = HandleMainStockChanged;
            _pageNumbers = new ObservableCollection<int>();
            _cts = new CancellationTokenSource();

            SubscribeToEvents();
            InitializeCommands();
            _ = LoadDataAsync();
            Debug.WriteLine("ProductViewModel initialized");
        }

        private void InitializeCommands()
        {
            LoadCommand = new AsyncRelayCommand(async _ => await LoadDataAsync(), _ => !IsSaving);
            UpdateStockCommand = new AsyncRelayCommand(async _ => await UpdateStockAsync(), _ => !IsSaving);
            PrintBarcodeCommand = new AsyncRelayCommand(async _ => await PrintBarcodeAsync(), _ => !IsSaving);
         
            // Pagination commands
            NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => !IsLastPage);
            PreviousPageCommand = new RelayCommand(_ => CurrentPage--, _ => !IsFirstPage);
            GoToPageCommand = new RelayCommand<int>(page => CurrentPage = page);
            ChangePageSizeCommand = new RelayCommand<int>(size => PageSize = size);
        }

        // In SubscribeToEvents method
        protected override void SubscribeToEvents()
        {
            Debug.WriteLine("ProductViewModel: Subscribing to events");
            _eventAggregator.Subscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<MainStockDTO>>(_mainStockChangedHandler);
            _eventAggregator.Subscribe<ProductStockUpdatedEvent>(_productStockUpdatedHandler);
            Debug.WriteLine("ProductViewModel: Subscribed to all events");
        }

        // In UnsubscribeFromEvents method
        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<MainStockDTO>>(_mainStockChangedHandler);
            _eventAggregator.Unsubscribe<ProductStockUpdatedEvent>(_productStockUpdatedHandler);
        }

        // In ProductViewModel.cs
        private async void HandleProductStockUpdated(ProductStockUpdatedEvent evt)
        {
            Debug.WriteLine($"ProductViewModel: Product stock updated - ID: {evt.ProductId}, New Stock: {evt.NewStock}");

            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Find the product in the collection and update its stock
                    var product = Products.FirstOrDefault(p => p.ProductId == evt.ProductId);
                    if (product != null)
                    {
                        // Update the stock value
                        product.CurrentStock = (int)Math.Round(evt.NewStock);
                        Debug.WriteLine($"ProductViewModel: Updated product {product.Name} stock to {product.CurrentStock}");

                        // If this is the selected product, update calculations
                        if (SelectedProduct != null && SelectedProduct.ProductId == evt.ProductId)
                        {
                            SelectedProduct.CurrentStock = (int)Math.Round(evt.NewStock);
                            CalculateSelectedProductValues();
                        }

                        // Recalculate all aggregated values
                        CalculateAggregatedValues();
                    }
                    else
                    {
                        // Product not in current view - force a reload
                        Debug.WriteLine("ProductViewModel: Product not found in current page, forcing reload");
                        Task.Run(async () => await SafeLoadDataAsync());
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProductViewModel: Error handling product stock update: {ex.Message}");
            }
        }
        private void SelectedProduct_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // When price properties or stock change, recalculate values
            if (e.PropertyName == nameof(ProductDTO.PurchasePrice) ||
                e.PropertyName == nameof(ProductDTO.SalePrice) ||
                e.PropertyName == nameof(ProductDTO.CurrentStock))
            {
                CalculateSelectedProductValues();
            }
        }

        private void HandleMainStockChanged(EntityChangedEvent<MainStockDTO> evt)
        {
            // Refresh MainStock items when they change
            _ = LoadMainStockItemsAsync();
        }

        private async void HandleCategoryChanged(EntityChangedEvent<CategoryDTO> evt)
        {
            try
            {
                Debug.WriteLine("ProductViewModel: Handling category change");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    switch (evt.Action)
                    {
                        case "Create":
                            // Only add if the category is active
                            if (evt.Entity.IsActive && !Categories.Any(c => c.CategoryId == evt.Entity.CategoryId))
                            {
                                Categories.Add(evt.Entity);
                                Debug.WriteLine($"Added new category {evt.Entity.Name}");
                            }
                            break;
                        case "Update":
                            var existingIndex = Categories.ToList().FindIndex(c => c.CategoryId == evt.Entity.CategoryId);
                            if (existingIndex != -1)
                            {
                                if (evt.Entity.IsActive)
                                {
                                    // Update the existing category if it's active
                                    Categories[existingIndex] = evt.Entity;
                                    Debug.WriteLine($"Updated category {evt.Entity.Name}");
                                }
                                else
                                {
                                    // Remove the category if it's now inactive
                                    Categories.RemoveAt(existingIndex);
                                    Debug.WriteLine($"Removed inactive category {evt.Entity.Name}");
                                }
                            }
                            else if (evt.Entity.IsActive)
                            {
                                // This is a category that wasn't in our list but is now active
                                Categories.Add(evt.Entity);
                                Debug.WriteLine($"Added newly active category {evt.Entity.Name}");
                            }
                            break;
                        case "Delete":
                            var categoryToRemove = Categories.FirstOrDefault(c => c.CategoryId == evt.Entity.CategoryId);
                            if (categoryToRemove != null)
                            {
                                Categories.Remove(categoryToRemove);
                                Debug.WriteLine($"Removed category {categoryToRemove.Name}");
                            }
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProductViewModel: Error handling category change: {ex.Message}");
            }
        }
        private async void HandleProductChanged(EntityChangedEvent<ProductDTO> evt)
        {
            try
            {
                Debug.WriteLine($"Handling {evt.Action} event for product");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    switch (evt.Action)
                    {
                        case "Create":
                            Debug.WriteLine("Adding new product to collection");
                            if (!Products.Any(p => p.ProductId == evt.Entity.ProductId))
                            {
                                Products.Add(evt.Entity);
                                Debug.WriteLine("Product added to collection");
                            }
                            break;

                        case "Update":
                            Debug.WriteLine("Updating product in collection");
                            var existingProduct = Products.FirstOrDefault(p => p.ProductId == evt.Entity.ProductId);
                            if (existingProduct != null)
                            {
                                var index = Products.IndexOf(existingProduct);
                                Products[index] = evt.Entity;
                                Debug.WriteLine("Product updated in collection");
                            }
                            else
                            {
                                // ADDED: Force reload if product not in current page but is relevant
                                // to current filter/search criteria
                                if (string.IsNullOrWhiteSpace(SearchText) ||
                                    evt.Entity.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                    evt.Entity.Barcode.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                    evt.Entity.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Schedule a reload without blocking the UI thread
                                    Task.Run(async () => await SafeLoadDataAsync());
                                    Debug.WriteLine("Scheduled reload for updated product not in current view");
                                }
                            }
                            break;

                        case "Delete":
                            Debug.WriteLine("Removing product from collection");
                            var productToRemove = Products.FirstOrDefault(p => p.ProductId == evt.Entity.ProductId);
                            if (productToRemove != null)
                            {
                                Products.Remove(productToRemove);
                                Debug.WriteLine("Product removed from collection");
                            }
                            break;
                    }

                    // Update calculations when products change
                    CalculateAggregatedValues();

                    // Refresh filtered products if we're using search
                    if (!string.IsNullOrWhiteSpace(SearchText))
                    {
                        FilterProducts();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Product refresh error: {ex.Message}");
            }
        }
        private void UpdateVisiblePageNumbers()
        {
            var visiblePages = new List<int>();
            int startPage = Math.Max(1, CurrentPage - 2);
            int endPage = Math.Min(TotalPages, CurrentPage + 2);

            // Always show first page
            if (startPage > 1)
            {
                visiblePages.Add(1);
                if (startPage > 2) visiblePages.Add(-1); // -1 represents ellipsis
            }

            // Add current range
            for (int i = startPage; i <= endPage; i++)
            {
                visiblePages.Add(i);
            }

            // Always show last page
            if (endPage < TotalPages)
            {
                if (endPage < TotalPages - 1) visiblePages.Add(-1); // -1 represents ellipsis
                visiblePages.Add(TotalPages);
            }

            VisiblePageNumbers = visiblePages;
            OnPropertyChanged(nameof(VisiblePageNumbers));
        }

        // Calculate values for the selected product
        private void CalculateSelectedProductValues()
        {
            try
            {
                if (SelectedProduct == null)
                {
                    SelectedProductTotalCost = 0;
                    SelectedProductTotalValue = 0;
                    SelectedProductProfitMargin = 0;
                    SelectedProductProfitPercentage = 0;
                    return;
                }

                // Calculate total cost (purchase price × stock)
                SelectedProductTotalCost = SelectedProduct.PurchasePrice * SelectedProduct.CurrentStock;

                // Calculate total value (sale price × stock)
                SelectedProductTotalValue = SelectedProduct.SalePrice * SelectedProduct.CurrentStock;

                // Calculate profit margin (sale value - purchase value)
                SelectedProductProfitMargin = SelectedProductTotalValue - SelectedProductTotalCost;

                // Calculate profit percentage
                if (SelectedProductTotalCost > 0)
                {
                    SelectedProductProfitPercentage = (SelectedProductProfitMargin / SelectedProductTotalCost) * 100;
                }
                else
                {
                    SelectedProductProfitPercentage = 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating selected product values: {ex.Message}");
                SelectedProductTotalCost = 0;
                SelectedProductTotalValue = 0;
                SelectedProductProfitMargin = 0;
                SelectedProductProfitPercentage = 0;
            }
        }

        // Method to calculate aggregated values for all products
        private void CalculateAggregatedValues()
        {
            try
            {
                if (Products == null || !Products.Any())
                {
                    TotalPurchaseValue = 0;
                    TotalSaleValue = 0;
                    TotalProfit = 0;
                    return;
                }

                decimal totalPurchase = 0;
                decimal totalSale = 0;

                foreach (var product in Products)
                {
                    try
                    {
                        totalPurchase += Math.Round(product.PurchasePrice * product.CurrentStock, 2);
                        totalSale += Math.Round(product.SalePrice * product.CurrentStock, 2);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error calculating values for product {product.Name}: {ex.Message}");
                        // Continue with other products even if one fails
                    }
                }

                TotalPurchaseValue = totalPurchase;
                TotalSaleValue = totalSale;
                TotalProfit = totalSale - totalPurchase;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating aggregated values: {ex.Message}");
                TotalPurchaseValue = 0;
                TotalSaleValue = 0;
                TotalProfit = 0;
            }
        }

        private async Task SafeLoadDataAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                Debug.WriteLine("SafeLoadDataAsync skipped - already in progress");
                return;
            }

            // Create a new CancellationTokenSource for this operation
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                IsSaving = true;
                StatusMessage = "Loading data...";

                try
                {
                    // Add a timeout for the operation
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                    // Get categories and suppliers (these are always fetched in full)
                    var categoriesTask = _categoryService.GetActiveAsync();
                    var suppliersTask = _supplierService.GetActiveAsync();

                    // Load MainStock items for receiving
                    var mainStockTask = _mainStockService.GetAllAsync();

                    // Get total count of products
                    var totalCount = await GetTotalProductCount();
                    if (linkedCts.Token.IsCancellationRequested) return;

                    // Calculate total pages
                    int calculatedTotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
                    TotalPages = calculatedTotalPages;
                    TotalProducts = totalCount;

                    // Get paginated products
                    var products = await GetPagedProducts(CurrentPage, PageSize, SearchText);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    // Wait for categories, suppliers, and MainStock to complete
                    await Task.WhenAll(categoriesTask, suppliersTask, mainStockTask);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    var categories = await categoriesTask;
                    var suppliers = await suppliersTask;
                    var mainStockItems = await mainStockTask;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!linkedCts.Token.IsCancellationRequested)
                        {
                            Products = new ObservableCollection<ProductDTO>(products);
                            FilteredProducts = new ObservableCollection<ProductDTO>(products);
                            Categories = new ObservableCollection<CategoryDTO>(categories);
                            Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                            MainStockItems = new ObservableCollection<MainStockDTO>(mainStockItems);

                            // Calculate values after loading products
                            CalculateAggregatedValues();

                            if (SelectedProduct != null)
                            {
                                CalculateSelectedProductValues();
                            }
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("Operation was canceled");
                }
                catch (Exception ex)
                {
                    ShowTemporaryErrorMessage($"Error loading data: {ex.Message}");
                }
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }

        private async Task<int> GetTotalProductCount()
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                // If searching, we need to get all products and count filtered ones
                var allProducts = await _productService.GetAllAsync();
                return allProducts.Count(p =>
                    p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    p.Barcode.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    p.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    p.SupplierName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    (p.Speed?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            else
            {
                // If not searching, we can just get the total count
                var allProducts = await _productService.GetAllAsync();
                return allProducts.Count();
            }
        }

        private async Task<List<ProductDTO>> GetPagedProducts(int page, int pageSize, string searchText)
        {
            // Get all products (in a real implementation, this should be done in the backend)
            var allProducts = await _productService.GetAllAsync();

            // Filter if needed
            IEnumerable<ProductDTO> filteredProducts = allProducts;
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filteredProducts = allProducts.Where(p =>
                    p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.Barcode.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.CategoryName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.SupplierName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    (p.Speed?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            // Apply pagination
            return filteredProducts
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
     

       
        private async Task LoadMainStockItemsAsync()
        {
            try
            {
                var items = await _mainStockService.GetAllAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MainStockItems = new ObservableCollection<MainStockDTO>(items);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading MainStock items: {ex.Message}");
            }
        }

        protected override async Task LoadDataAsync()
        {
            await SafeLoadDataAsync();
        }

        private void FilterProducts()
        {
            _ = SafeLoadDataAsync();
        }

     
    
        private async Task UpdateStockAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Stock update operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (SelectedProduct == null || StockIncrement <= 0)
                {
                    ShowTemporaryErrorMessage("Please select a product and enter a valid stock increment.");
                    return;
                }

                IsSaving = true;
                StatusMessage = "Updating stock...";

                var newStock = SelectedProduct.CurrentStock + StockIncrement;
                SelectedProduct.CurrentStock = newStock;

                await _productService.UpdateStockAsync(SelectedProduct.ProductId, StockIncrement);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Stock updated successfully. New stock: {newStock}",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                });

                StockIncrement = 0;

                // Recalculate values after stock update
                CalculateSelectedProductValues();
                CalculateAggregatedValues();
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error updating stock: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }

        private async Task PrintBarcodeAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("A print operation is already in progress. Please wait.");
                return;
            }

            try
            {
                if (SelectedProduct == null)
                {
                    ShowTemporaryErrorMessage("Please select a product first.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedProduct.Barcode))
                {
                    ShowTemporaryErrorMessage("This product does not have a barcode assigned.");
                    return;
                }

                StatusMessage = "Print functionality is view-only.";
                await Task.Delay(2000);

                // In real implementation, this would print the barcode
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        "Print barcode functionality is available in view-only mode. Product management has been moved to MainStock.",
                        "Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in barcode printing: {ex.Message}");
                ShowTemporaryErrorMessage($"Error printing barcode: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }

        private Window GetOwnerWindow()
        {
            // Try to get the active window first
            var activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (activeWindow != null)
                return activeWindow;

            // Fall back to the main window
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.IsLoaded)
                return mainWindow;

            // Last resort, get any window that's visible
            return System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                   ?? System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault();
        }

        private void ShowTemporaryErrorMessage(string message)
        {
            StatusMessage = message;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });

            // Automatically clear error after delay
            Task.Run(async () =>
            {
                await Task.Delay(5000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (StatusMessage == message) // Only clear if still the same message
                    {
                        StatusMessage = string.Empty;
                    }
                });
            });
        }

        public override void Dispose()
        {
            if (!_isDisposed)
            {
                // Unsubscribe from the property changed event of the selected product
                if (SelectedProduct != null)
                {
                    SelectedProduct.PropertyChanged -= SelectedProduct_PropertyChanged;
                }

                _cts?.Cancel();
                _cts?.Dispose();
                _operationLock?.Dispose();
                UnsubscribeFromEvents();

                _isDisposed = true;
            }

            base.Dispose();
        }
    }
}