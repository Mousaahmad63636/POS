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
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Windows.Documents;
using System.Printing;
using System.Windows.Media;
using System.Windows.Markup;
using Microsoft.Win32;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using System.Text;
using QuickTechSystems.Application.Services;
using System.Printing;

namespace QuickTechSystems.WPF.ViewModels
{
    public class ProductViewModel : ViewModelBase
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IBarcodeService _barcodeService;
        private readonly ISupplierService _supplierService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;
        private FlowDirection _flowDirection = FlowDirection.LeftToRight;
        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<ProductDTO> _filteredProducts;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private ObservableCollection<CategoryDTO> _plantsHardscapeCategories;
        private ObservableCollection<CategoryDTO> _localImportedCategories;
        private ObservableCollection<CategoryDTO> _indoorOutdoorCategories;
        private ObservableCollection<CategoryDTO> _plantFamilyCategories;
        private ObservableCollection<CategoryDTO> _detailCategories;
        private ProductDTO? _selectedProduct;
        private bool _isEditing;
        private BitmapImage? _barcodeImage;
        private string _searchText = string.Empty;
        private bool _isSaving;
        private string _statusMessage = string.Empty;
        private int _stockIncrement;
        private Dictionary<int, List<string>> _validationErrors;
        private Action<EntityChangedEvent<ProductDTO>> _productChangedHandler;
        private readonly Action<EntityChangedEvent<SupplierDTO>> _supplierChangedHandler;
        private readonly Action<EntityChangedEvent<CategoryDTO>> _categoryChangedHandler;
        private int _labelsPerProduct = 1;
        private BitmapImage? _productImage;
        private bool _isProductPopupOpen;
        private bool _isNewProduct;
        private decimal _totalProfit;
        private CancellationTokenSource _cts;
        private readonly IImagePathService _imagePathService;
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalPages;
        private ObservableCollection<int> _pageNumbers;
        private List<int> _visiblePageNumbers = new List<int>();
        private int _totalProducts;
        private decimal _totalPurchaseValue;
        private decimal _totalSaleValue;
        private decimal _selectedProductTotalCost;
        private decimal _selectedProductTotalValue;
        private decimal _selectedProductProfitMargin;
        private decimal _selectedProductProfitPercentage;

        public decimal TotalProfit
        {
            get => _totalProfit;
            set => SetProperty(ref _totalProfit, value);
        }

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

        public FlowDirection FlowDirection
        {
            get => _flowDirection;
            set => SetProperty(ref _flowDirection, value);
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

        public BitmapImage? ProductImage
        {
            get => _productImage;
            set => SetProperty(ref _productImage, value);
        }

        public int LabelsPerProduct
        {
            get => _labelsPerProduct;
            set => SetProperty(ref _labelsPerProduct, Math.Max(1, value));
        }

        public bool IsProductPopupOpen
        {
            get => _isProductPopupOpen;
            set => SetProperty(ref _isProductPopupOpen, value);
        }

        public bool IsNewProduct
        {
            get => _isNewProduct;
            set => SetProperty(ref _isNewProduct, value);
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

        public ObservableCollection<SupplierDTO> Suppliers
        {
            get => _suppliers;
            set => SetProperty(ref _suppliers, value);
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

        public ProductDTO? SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (_selectedProduct != null)
                {
                    _selectedProduct.PropertyChanged -= SelectedProduct_PropertyChanged;
                }

                SetProperty(ref _selectedProduct, value);
                IsEditing = value != null;

                if (value != null)
                {
                    value.PropertyChanged += SelectedProduct_PropertyChanged;

                    if (value.BarcodeImage != null)
                    {
                        LoadBarcodeImage(value.BarcodeImage);
                    }
                    else
                    {
                        BarcodeImage = null;
                    }

                    ProductImage = value.ImagePath != null ? LoadImageFromPath(value.ImagePath) : null;
                    CalculateSelectedProductValues();
                }
                else
                {
                    BarcodeImage = null;
                    ProductImage = null;
                    SelectedProductTotalCost = 0;
                    SelectedProductTotalValue = 0;
                    SelectedProductProfitMargin = 0;
                    SelectedProductProfitPercentage = 0;
                }
            }
        }

        private BitmapImage? LoadImageFromPath(string? imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return null;

            try
            {
                string fullPath;

                if (_imagePathService != null)
                {
                    fullPath = _imagePathService.GetFullImagePath(imagePath);
                }
                else
                {
                    if (Path.IsPathRooted(imagePath))
                    {
                        fullPath = imagePath;
                    }
                    else
                    {
                        fullPath = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "ProductImages",
                            imagePath
                        );
                    }
                }

                if (!File.Exists(fullPath))
                {
                    Debug.WriteLine($"Image file not found: {fullPath}");
                    return null;
                }

                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;

                Uri fileUri = new Uri("file:///" + fullPath.Replace('\\', '/'));
                image.UriSource = fileUri;

                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image from path: {ex.Message}");

                try
                {
                    string retryPath;
                    if (_imagePathService != null)
                    {
                        retryPath = _imagePathService.GetFullImagePath(imagePath);
                    }
                    else if (Path.IsPathRooted(imagePath))
                    {
                        retryPath = imagePath;
                    }
                    else
                    {
                        retryPath = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "ProductImages",
                            imagePath
                        );
                    }

                    BitmapImage fallbackImage = new BitmapImage();

                    using (var stream = new FileStream(retryPath, FileMode.Open, FileAccess.Read))
                    {
                        fallbackImage.BeginInit();
                        fallbackImage.CacheOption = BitmapCacheOption.OnLoad;
                        fallbackImage.StreamSource = stream;
                        fallbackImage.EndInit();
                        fallbackImage.Freeze();
                    }

                    return fallbackImage;
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"Fallback image loading also failed: {fallbackEx.Message}");
                    return null;
                }
            }
        }

        private void SelectedProduct_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProductDTO.PurchasePrice) ||
                e.PropertyName == nameof(ProductDTO.SalePrice) ||
                e.PropertyName == nameof(ProductDTO.CurrentStock))
            {
                CalculateSelectedProductValues();
            }
            else if (e.PropertyName == nameof(ProductDTO.PlantsHardscapeId))
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

            var category = PlantsHardscapeCategories?.FirstOrDefault(c => c.CategoryId == product.PlantsHardscapeId);
            if (category != null)
            {
                product.PlantsHardscapeName = category.Name;
            }
        }

        private void UpdateLocalImportedName(ProductDTO product)
        {
            if (product == null || !product.LocalImportedId.HasValue || product.LocalImportedId <= 0) return;

            var category = LocalImportedCategories?.FirstOrDefault(c => c.CategoryId == product.LocalImportedId);
            if (category != null)
            {
                product.LocalImportedName = category.Name;
            }
        }

        private void UpdateIndoorOutdoorName(ProductDTO product)
        {
            if (product == null || !product.IndoorOutdoorId.HasValue || product.IndoorOutdoorId <= 0) return;

            var category = IndoorOutdoorCategories?.FirstOrDefault(c => c.CategoryId == product.IndoorOutdoorId);
            if (category != null)
            {
                product.IndoorOutdoorName = category.Name;
            }
        }

        private void UpdatePlantFamilyName(ProductDTO product)
        {
            if (product == null || !product.PlantFamilyId.HasValue || product.PlantFamilyId <= 0) return;

            var category = PlantFamilyCategories?.FirstOrDefault(c => c.CategoryId == product.PlantFamilyId);
            if (category != null)
            {
                product.PlantFamilyName = category.Name;
            }
        }

        private void UpdateDetailName(ProductDTO product)
        {
            if (product == null || !product.DetailId.HasValue || product.DetailId <= 0) return;

            var category = DetailCategories?.FirstOrDefault(c => c.CategoryId == product.DetailId);
            if (category != null)
            {
                product.DetailName = category.Name;
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public BitmapImage? BarcodeImage
        {
            get => _barcodeImage;
            set
            {
                if (_barcodeImage != value)
                {
                    _barcodeImage = value;
                    OnPropertyChanged(nameof(BarcodeImage));
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
                    _currentPage = 1;
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
                    _currentPage = 1;
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

        public ICommand BulkAddCommand { get; private set; }
        public ICommand LoadCommand { get; private set; }
        public ICommand AddCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand GenerateBarcodeCommand { get; private set; }
        public ICommand GenerateAutomaticBarcodeCommand { get; private set; }
        public ICommand UpdateStockCommand { get; private set; }
        public ICommand PrintBarcodeCommand { get; private set; }
        public ICommand GenerateMissingBarcodesCommand { get; private set; }
        public ICommand UploadImageCommand { get; private set; }
        public ICommand ClearImageCommand { get; private set; }
        public ICommand NextPageCommand { get; private set; }
        public ICommand PreviousPageCommand { get; private set; }
        public ICommand GoToPageCommand { get; private set; }
        public ICommand ChangePageSizeCommand { get; private set; }

        public ProductViewModel(
            IProductService productService,
            ICategoryService categoryService,
            IBarcodeService barcodeService,
            ISupplierService supplierService,
            IImagePathService imagePathService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            Debug.WriteLine("Initializing ProductViewModel");
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _barcodeService = barcodeService ?? throw new ArgumentNullException(nameof(barcodeService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));
            _imagePathService = imagePathService ?? throw new ArgumentNullException(nameof(imagePathService));

            _products = new ObservableCollection<ProductDTO>();
            _filteredProducts = new ObservableCollection<ProductDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();
            _plantsHardscapeCategories = new ObservableCollection<CategoryDTO>();
            _localImportedCategories = new ObservableCollection<CategoryDTO>();
            _indoorOutdoorCategories = new ObservableCollection<CategoryDTO>();
            _plantFamilyCategories = new ObservableCollection<CategoryDTO>();
            _detailCategories = new ObservableCollection<CategoryDTO>();
            _validationErrors = new Dictionary<int, List<string>>();
            _productChangedHandler = HandleProductChanged;
            _categoryChangedHandler = HandleCategoryChanged;
            _supplierChangedHandler = HandleSupplierChanged;
            _pageNumbers = new ObservableCollection<int>();
            _cts = new CancellationTokenSource();

            SubscribeToEvents();
            InitializeCommands();
            _ = LoadDataAsync();
            Debug.WriteLine("ProductViewModel initialized");
        }

        private void UpdateVisiblePageNumbers()
        {
            var visiblePages = new List<int>();
            int startPage = Math.Max(1, CurrentPage - 2);
            int endPage = Math.Min(TotalPages, CurrentPage + 2);

            if (startPage > 1)
            {
                visiblePages.Add(1);
                if (startPage > 2) visiblePages.Add(-1);
            }

            for (int i = startPage; i <= endPage; i++)
            {
                visiblePages.Add(i);
            }

            if (endPage < TotalPages)
            {
                if (endPage < TotalPages - 1) visiblePages.Add(-1);
                visiblePages.Add(TotalPages);
            }

            VisiblePageNumbers = visiblePages;
            OnPropertyChanged(nameof(VisiblePageNumbers));
        }

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

                SelectedProductTotalCost = SelectedProduct.PurchasePrice * SelectedProduct.CurrentStock;
                SelectedProductTotalValue = SelectedProduct.SalePrice * SelectedProduct.CurrentStock;
                SelectedProductProfitMargin = SelectedProductTotalValue - SelectedProductTotalCost;

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

        private void InitializeCommands()
        {
            LoadCommand = new AsyncRelayCommand(async _ => await LoadDataAsync(), _ => !IsSaving);
            AddCommand = new RelayCommand(_ => AddNew(), _ => !IsSaving);
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync(), _ => !IsSaving);
            DeleteCommand = new AsyncRelayCommand(async _ => await DeleteAsync(), _ => !IsSaving);
            GenerateBarcodeCommand = new RelayCommand(_ => GenerateBarcode(), _ => !IsSaving);
            GenerateAutomaticBarcodeCommand = new RelayCommand(_ => GenerateAutomaticBarcode(), _ => !IsSaving);
            BulkAddCommand = new AsyncRelayCommand(async _ => await ShowBulkAddDialog(), _ => !IsSaving);
            UpdateStockCommand = new AsyncRelayCommand(async _ => await UpdateStockAsync(), _ => !IsSaving);
            PrintBarcodeCommand = new AsyncRelayCommand(async _ => await PrintBarcodeAsync(), _ => !IsSaving);
            UploadImageCommand = new RelayCommand(_ => UploadImage());
            ClearImageCommand = new RelayCommand(_ => ClearImage());
            GenerateMissingBarcodesCommand = new AsyncRelayCommand(async _ => await GenerateMissingBarcodeImages(), _ => !IsSaving);

            NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => !IsLastPage);
            PreviousPageCommand = new RelayCommand(_ => CurrentPage--, _ => !IsFirstPage);
            GoToPageCommand = new RelayCommand<int>(page => CurrentPage = page);
            ChangePageSizeCommand = new RelayCommand<int>(size => PageSize = size);
        }

        protected override void SubscribeToEvents()
        {
            Debug.WriteLine("ProductViewModel: Subscribing to events");
            _eventAggregator.Subscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<SupplierDTO>>(_supplierChangedHandler);
            Debug.WriteLine("ProductViewModel: Subscribed to all events");
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<SupplierDTO>>(_supplierChangedHandler);
        }

        public void ShowProductPopup()
        {
            try
            {
                var productWindow = new ProductDetailsWindow
                {
                    DataContext = this,
                    Owner = GetOwnerWindow()
                };

                productWindow.SaveCompleted += ProductDetailsWindow_SaveCompleted;
                productWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing product window: {ex.Message}");
                ShowTemporaryErrorMessage($"Error displaying product details: {ex.Message}");
            }
        }

        private void ProductDetailsWindow_SaveCompleted(object sender, RoutedEventArgs e)
        {
        }

        public void CloseProductPopup()
        {
            IsProductPopupOpen = false;
        }

        public void EditProduct(ProductDTO product)
        {
            if (product != null)
            {
                SelectedProduct = product;
                IsNewProduct = false;
                ShowProductPopup();
            }
        }

        public async Task RefreshTransactionProductLists()
        {
            try
            {
                var product = SelectedProduct;
                if (product != null)
                {
                    _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", product));
                    Debug.WriteLine($"Published refresh event for product: {product.Name}, IsActive: {product.IsActive}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing transaction product lists: {ex.Message}");
            }
        }

        private void UploadImage()
        {
            bool wasPopupOpen = IsProductPopupOpen;

            if (wasPopupOpen)
            {
                IsProductPopupOpen = false;
            }

            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Image files (*.jpg, *.jpeg, *.png) | *.jpg; *.jpeg; *.png",
                    Title = "Select product image"
                };

                var ownerWindow = GetOwnerWindow();

                bool? result = openFileDialog.ShowDialog(ownerWindow);

                if (result == true && SelectedProduct != null)
                {
                    try
                    {
                        string sourcePath = openFileDialog.FileName;
                        string savedPath = _imagePathService.SaveProductImage(sourcePath);

                        SelectedProduct.ImagePath = savedPath;
                        ProductImage = LoadImageFromPath(savedPath);

                        Debug.WriteLine($"Image saved at: {savedPath}");
                    }
                    catch (Exception ex)
                    {
                        ShowTemporaryErrorMessage($"Error loading image: {ex.Message}");
                    }
                }
            }
            finally
            {
                if (wasPopupOpen)
                {
                    IsProductPopupOpen = true;
                }
            }
        }

        private void ClearImage()
        {
            if (SelectedProduct != null)
            {
                if (!string.IsNullOrEmpty(SelectedProduct.ImagePath))
                {
                    _imagePathService.DeleteProductImage(SelectedProduct.ImagePath);
                }

                SelectedProduct.ImagePath = null;
                ProductImage = null;
            }
        }

        private BitmapImage? LoadImage(byte[]? imageData)
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
                    image.Freeze();
                }
                return image;
            }
            catch
            {
                return null;
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

                StatusMessage = "Preparing barcode...";
                IsSaving = true;

                if (SelectedProduct.BarcodeImage == null)
                {
                    try
                    {
                        var barcodeData = _barcodeService.GenerateBarcode(SelectedProduct.Barcode, 600, 200);
                        if (barcodeData == null)
                        {
                            ShowTemporaryErrorMessage("Failed to generate barcode.");
                            return;
                        }

                        SelectedProduct.BarcodeImage = barcodeData;
                        BarcodeImage = LoadBarcodeImage(barcodeData);

                        var productCopy = new ProductDTO
                        {
                            ProductId = SelectedProduct.ProductId,
                            Name = SelectedProduct.Name,
                            Barcode = SelectedProduct.Barcode,
                            CategoryId = SelectedProduct.CategoryId,
                            CategoryName = SelectedProduct.CategoryName,
                            SupplierId = SelectedProduct.SupplierId,
                            SupplierName = SelectedProduct.SupplierName,
                            Description = SelectedProduct.Description,
                            PurchasePrice = SelectedProduct.PurchasePrice,
                            SalePrice = SelectedProduct.SalePrice,
                            CurrentStock = SelectedProduct.CurrentStock,
                            MinimumStock = SelectedProduct.MinimumStock,
                            BarcodeImage = barcodeData,
                            Speed = SelectedProduct.Speed,
                            IsActive = SelectedProduct.IsActive,
                            ImagePath = SelectedProduct.ImagePath,
                            CreatedAt = SelectedProduct.CreatedAt,
                            UpdatedAt = DateTime.Now
                        };

                        await _productService.UpdateAsync(productCopy);
                    }
                    catch (Exception ex)
                    {
                        ShowTemporaryErrorMessage($"Error generating barcode: {ex.Message}");
                        return;
                    }
                }

                bool printerCancelled = false;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        StatusMessage = "Preparing barcode labels...";

                        var printDialog = new PrintDialog();
                        if (printDialog.ShowDialog() != true)
                        {
                            printerCancelled = true;
                            return;
                        }

                        printDialog.PrintTicket.PageMediaSize = new PageMediaSize(
                            (int)(5.5 * 39.37),
                            (int)(4.0 * 39.37)
                        );
                        printDialog.PrintTicket.PageBorderless = PageBorderless.Borderless;
                        printDialog.PrintTicket.PageMediaType = PageMediaType.Label;

                        var fixedDocument = new FixedDocument();

                        StatusMessage = $"Creating document with {LabelsPerProduct} labels...";

                        for (int i = 0; i < LabelsPerProduct; i++)
                        {
                            var pageContent = new PageContent();
                            var fixedPage = new FixedPage();

                            fixedPage.Width = printDialog.PrintableAreaWidth;
                            fixedPage.Height = printDialog.PrintableAreaHeight;

                            var labelVisual = CreateBarcodeLabelVisual(SelectedProduct,
                                printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);

                            fixedPage.Children.Add(labelVisual);

                            ((IAddChild)pageContent).AddChild(fixedPage);
                            fixedDocument.Pages.Add(pageContent);
                        }

                        StatusMessage = "Sending to printer...";
                        printDialog.PrintDocument(fixedDocument.DocumentPaginator,
                            $"Barcode Labels - {SelectedProduct.Name} ({LabelsPerProduct})");

                        StatusMessage = "Barcode labels printed successfully.";
                        await Task.Delay(2000);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error printing barcodes: {ex.Message}");
                        ShowTemporaryErrorMessage($"Error printing barcodes: {ex.Message}");
                    }
                });

                if (printerCancelled)
                {
                    StatusMessage = "Printing cancelled by user.";
                    await Task.Delay(1000);
                }
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

        private UIElement CreateBarcodeLabelVisual(ProductDTO product, double width, double height)
        {
            var outerCanvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = Brushes.White
            };

            var canvas = new Canvas
            {
                Width = width,
                Height = height - 15
            };

            Canvas.SetTop(canvas, 15);
            outerCanvas.Children.Add(canvas);

            double barcodeWidth = width * 0.9;
            double barcodeHeight = height * 0.5;

            try
            {
                if (product == null)
                {
                    throw new ArgumentNullException("product", "Product cannot be null");
                }

                string displayBarcode = product.Barcode ?? "N/A";
                if (!string.IsNullOrEmpty(displayBarcode) && displayBarcode.Length > 12)
                {
                    Debug.WriteLine($"Warning: Barcode '{displayBarcode}' exceeds 12 digits. It may not scan correctly.");
                }

                var nameText = product.Name ?? "Unknown Product";
                var nameTextBlock = new TextBlock
                {
                    Text = nameText,
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Width = width * 0.9,
                    MaxHeight = height * 0.15
                };

                Canvas.SetLeft(nameTextBlock, (width - nameTextBlock.Width) / 2);
                Canvas.SetTop(nameTextBlock, 0);
                canvas.Children.Add(nameTextBlock);

                double barcodeTop = height * 0.15;

                BitmapImage bitmapSource = null;
                if (product.BarcodeImage != null)
                {
                    bitmapSource = LoadBarcodeImage(product.BarcodeImage);
                }

                if (bitmapSource == null)
                {
                    var placeholder = new Border
                    {
                        Width = barcodeWidth,
                        Height = barcodeHeight,
                        Background = Brushes.LightGray,
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(1)
                    };

                    var placeholderText = new TextBlock
                    {
                        Text = "Barcode Image\nNot Available",
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 10,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    };

                    placeholder.Child = placeholderText;

                    Canvas.SetLeft(placeholder, (width - barcodeWidth) / 2);
                    Canvas.SetTop(placeholder, barcodeTop);
                    canvas.Children.Add(placeholder);
                }
                else
                {
                    var barcodeImage = new Image
                    {
                        Source = bitmapSource,
                        Width = barcodeWidth,
                        Height = barcodeHeight,
                        Stretch = Stretch.Uniform,
                        SnapsToDevicePixels = true
                    };

                    RenderOptions.SetBitmapScalingMode(barcodeImage, BitmapScalingMode.HighQuality);
                    RenderOptions.SetEdgeMode(barcodeImage, EdgeMode.Aliased);

                    Canvas.SetLeft(barcodeImage, (width - barcodeWidth) / 2);
                    Canvas.SetTop(barcodeImage, barcodeTop);
                    canvas.Children.Add(barcodeImage);
                }

                var barcodeTextBlock = new TextBlock
                {
                    Text = displayBarcode,
                    FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                    FontSize = 9,
                    TextAlignment = TextAlignment.Center,
                    Width = width * 0.9
                };

                double barcodeImageBottom = barcodeTop + barcodeHeight;
                Canvas.SetLeft(barcodeTextBlock, (width - barcodeTextBlock.Width) / 2);
                Canvas.SetTop(barcodeTextBlock, barcodeImageBottom + 5);
                canvas.Children.Add(barcodeTextBlock);

                if (product.SalePrice > 0)
                {
                    var priceTextBlock = new TextBlock
                    {
                        Text = $"${product.SalePrice:N2}",
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        Width = width * 0.9
                    };

                    Canvas.SetLeft(priceTextBlock, (width - priceTextBlock.Width) / 2);
                    Canvas.SetTop(priceTextBlock, height * 0.75);
                    canvas.Children.Add(priceTextBlock);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating barcode visual: {ex.Message}");

                var errorTextBlock = new TextBlock
                {
                    Text = $"Error: {ex.Message}",
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 8,
                    TextWrapping = TextWrapping.Wrap,
                    Width = width * 0.9,
                    Foreground = Brushes.Red
                };

                Canvas.SetLeft(errorTextBlock, (width - errorTextBlock.Width) / 2);
                Canvas.SetTop(errorTextBlock, height * 0.7);
                canvas.Children.Add(errorTextBlock);
            }

            return outerCanvas;
        }

        private async Task ShowBulkAddDialog()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Bulk add operation already in progress. Please wait.");
                return;
            }

            try
            {
                IsSaving = true;
                StatusMessage = "Preparing bulk add dialog...";

                var viewModel = new BulkProductViewModel(
                    _productService,
                    _categoryService,
                    _supplierService,
                    _barcodeService,
                    _eventAggregator);

                var ownerWindow = GetOwnerWindow();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var dialog = new BulkProductDialog
                    {
                        DataContext = viewModel,
                        Owner = ownerWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    try
                    {
                        var result = dialog.ShowDialog();

                        if (result == true)
                        {
                            await LoadDataAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error showing bulk add dialog: {ex}");
                        ShowTemporaryErrorMessage($"Error showing bulk product dialog: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error preparing bulk add dialog: {ex}");
                ShowTemporaryErrorMessage($"Error in bulk add: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
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

                await _productService.UpdateAsync(SelectedProduct);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Stock updated successfully. New stock: {newStock}",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                });

                StockIncrement = 0;

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

        private Window GetOwnerWindow()
        {
            var activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (activeWindow != null)
                return activeWindow;

            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.IsLoaded)
                return mainWindow;

            return System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                   ?? System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault();
        }

        private async void HandleSupplierChanged(EntityChangedEvent<SupplierDTO> evt)
        {
            try
            {
                Debug.WriteLine($"ProductViewModel: Handling supplier change: {evt.Action}");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    switch (evt.Action)
                    {
                        case "Create":
                            if (evt.Entity.IsActive && !Suppliers.Any(s => s.SupplierId == evt.Entity.SupplierId))
                            {
                                Suppliers.Add(evt.Entity);
                                Debug.WriteLine($"Added new supplier {evt.Entity.Name}");
                            }
                            break;
                        case "Update":
                            var existingIndex = Suppliers.ToList().FindIndex(s => s.SupplierId == evt.Entity.SupplierId);
                            if (existingIndex != -1)
                            {
                                if (evt.Entity.IsActive)
                                {
                                    Suppliers[existingIndex] = evt.Entity;
                                    Debug.WriteLine($"Updated supplier {evt.Entity.Name}");
                                }
                                else
                                {
                                    Suppliers.RemoveAt(existingIndex);
                                    Debug.WriteLine($"Removed inactive supplier {evt.Entity.Name}");
                                }
                            }
                            else if (evt.Entity.IsActive)
                            {
                                Suppliers.Add(evt.Entity);
                                Debug.WriteLine($"Added newly active supplier {evt.Entity.Name}");
                            }
                            break;
                        case "Delete":
                            var supplierToRemove = Suppliers.FirstOrDefault(s => s.SupplierId == evt.Entity.SupplierId);
                            if (supplierToRemove != null)
                            {
                                Suppliers.Remove(supplierToRemove);
                                Debug.WriteLine($"Removed supplier {supplierToRemove.Name}");
                            }
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProductViewModel: Error handling supplier change: {ex.Message}");
            }
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
                            if (evt.Entity.IsActive && !Categories.Any(c => c.CategoryId == evt.Entity.CategoryId))
                            {
                                Categories.Add(evt.Entity);
                                Debug.WriteLine($"Added new category {evt.Entity.Name}");

                                LoadSpecializedCategoryCollections();
                            }
                            break;
                        case "Update":
                            var existingIndex = Categories.ToList().FindIndex(c => c.CategoryId == evt.Entity.CategoryId);
                            if (existingIndex != -1)
                            {
                                if (evt.Entity.IsActive)
                                {
                                    Categories[existingIndex] = evt.Entity;
                                    Debug.WriteLine($"Updated category {evt.Entity.Name}");
                                }
                                else
                                {
                                    Categories.RemoveAt(existingIndex);
                                    Debug.WriteLine($"Removed inactive category {evt.Entity.Name}");
                                }
                                LoadSpecializedCategoryCollections();
                            }
                            else if (evt.Entity.IsActive)
                            {
                                Categories.Add(evt.Entity);
                                Debug.WriteLine($"Added newly active category {evt.Entity.Name}");
                                LoadSpecializedCategoryCollections();
                            }
                            break;
                        case "Delete":
                            var categoryToRemove = Categories.FirstOrDefault(c => c.CategoryId == evt.Entity.CategoryId);
                            if (categoryToRemove != null)
                            {
                                Categories.Remove(categoryToRemove);
                                LoadSpecializedCategoryCollections();
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

                    CalculateAggregatedValues();

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

        private async Task SafeLoadDataAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                Debug.WriteLine("SafeLoadDataAsync skipped - already in progress");
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                IsSaving = true;
                StatusMessage = "Loading data...";

                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                    var categoriesTask = _categoryService.GetActiveAsync();
                    var suppliersTask = _supplierService.GetActiveAsync();

                    var totalCount = await GetTotalProductCount();
                    if (linkedCts.Token.IsCancellationRequested) return;

                    int calculatedTotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
                    TotalPages = calculatedTotalPages;
                    TotalProducts = totalCount;

                    var products = await GetPagedProducts(CurrentPage, PageSize, SearchText);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    await Task.WhenAll(categoriesTask, suppliersTask);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    var categories = await categoriesTask;
                    var suppliers = await suppliersTask;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!linkedCts.Token.IsCancellationRequested)
                        {
                            Products = new ObservableCollection<ProductDTO>(products);
                            FilteredProducts = new ObservableCollection<ProductDTO>(products);
                            Categories = new ObservableCollection<CategoryDTO>(categories);
                            Suppliers = new ObservableCollection<SupplierDTO>(suppliers);

                            LoadSpecializedCategoryCollections();

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

        private void LoadSpecializedCategoryCollections()
        {
            if (Categories == null) return;

            var categoryList = Categories.ToList();

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

            Debug.WriteLine($"Loaded specialized categories: PH={PlantsHardscapeCategories.Count}, LI={LocalImportedCategories.Count}, IO={IndoorOutdoorCategories.Count}, PF={PlantFamilyCategories.Count}, D={DetailCategories.Count}");
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

        private async Task<int> GetTotalProductCount()
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
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
                var allProducts = await _productService.GetAllAsync();
                return allProducts.Count();
            }
        }

        private async Task<List<ProductDTO>> GetPagedProducts(int page, int pageSize, string searchText)
        {
            var allProducts = await _productService.GetAllAsync();

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

            return filteredProducts
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        protected override async Task LoadDataAsync()
        {
            await SafeLoadDataAsync();
        }

        private void AddNew()
        {
            SelectedProduct = new ProductDTO
            {
                IsActive = true
            };
            BarcodeImage = null;
            ValidationErrors.Clear();
            IsNewProduct = true;
            ShowProductPopup();
        }

        private void FilterProducts()
        {
            _ = SafeLoadDataAsync();
        }

        public async Task SaveAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Save operation already in progress. Please wait.");
                return;
            }

            try
            {
                Debug.WriteLine("Starting save operation");
                if (SelectedProduct == null) return;

                IsSaving = true;
                StatusMessage = "Validating product...";

                var productToUpdate = SelectedProduct;

                if (string.IsNullOrWhiteSpace(productToUpdate.Barcode))
                {
                    Debug.WriteLine("No barcode provided, generating automatic barcode");

                    var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
                    var random = new Random();
                    var randomDigits = random.Next(1000, 9999).ToString();
                    var categoryPrefix = "001";

                    productToUpdate.Barcode = $"{categoryPrefix}-{timestamp}-{randomDigits}";
                }

                if (productToUpdate.BarcodeImage == null && !string.IsNullOrWhiteSpace(productToUpdate.Barcode))
                {
                    Debug.WriteLine("Generating barcode image for product");
                    try
                    {
                        productToUpdate.BarcodeImage = _barcodeService.GenerateBarcode(productToUpdate.Barcode);

                        BarcodeImage = LoadBarcodeImage(productToUpdate.BarcodeImage);
                        Debug.WriteLine("Barcode image generated successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                    }
                }

                if (!ValidateProduct(productToUpdate))
                {
                    return;
                }

                try
                {
                    var existingProduct = await _productService.FindProductByBarcodeAsync(
                        productToUpdate.Barcode,
                        productToUpdate.ProductId);

                    if (existingProduct != null)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(
                                $"Cannot save product: A product with barcode '{existingProduct.Barcode}' already exists: '{existingProduct.Name}'.",
                                "Duplicate Barcode",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking for duplicate barcode: {ex.Message}");
                }

                StatusMessage = "Saving product...";

                var productCopy = new ProductDTO
                {
                    ProductId = productToUpdate.ProductId,
                    Name = productToUpdate.Name,
                    Barcode = productToUpdate.Barcode,
                    CategoryId = 1,
                    CategoryName = productToUpdate.CategoryName,
                    SupplierId = productToUpdate.SupplierId,
                    SupplierName = productToUpdate.SupplierName,
                    Description = productToUpdate.Description,
                    PurchasePrice = productToUpdate.PurchasePrice,
                    SalePrice = productToUpdate.SalePrice,
                    CurrentStock = productToUpdate.CurrentStock,
                    MinimumStock = productToUpdate.MinimumStock,
                    BarcodeImage = productToUpdate.BarcodeImage,
                    Speed = productToUpdate.Speed,
                    IsActive = productToUpdate.IsActive,
                    ImagePath = productToUpdate.ImagePath,
                    PlantsHardscapeId = productToUpdate.PlantsHardscapeId,
                    PlantsHardscapeName = productToUpdate.PlantsHardscapeName,
                    LocalImportedId = productToUpdate.LocalImportedId,
                    LocalImportedName = productToUpdate.LocalImportedName,
                    IndoorOutdoorId = productToUpdate.IndoorOutdoorId,
                    IndoorOutdoorName = productToUpdate.IndoorOutdoorName,
                    PlantFamilyId = productToUpdate.PlantFamilyId,
                    PlantFamilyName = productToUpdate.PlantFamilyName,
                    DetailId = productToUpdate.DetailId,
                    DetailName = productToUpdate.DetailName,
                    CreatedAt = productToUpdate.CreatedAt,
                    UpdatedAt = DateTime.Now
                };

                try
                {
                    if (productToUpdate.ProductId == 0)
                    {
                        var result = await _productService.CreateAsync(productCopy);

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                            SelectedProduct = result;
                        });

                        productToUpdate = result;
                    }
                    else
                    {
                        await _productService.UpdateAsync(productCopy);
                    }

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Product saved successfully.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    });

                    CalculateAggregatedValues();
                    CalculateSelectedProductValues();

                    CloseProductPopup();

                    await RefreshSpecificProduct(productToUpdate.ProductId);
                    await RefreshTransactionProductLists();
                    await SafeLoadDataAsync();

                    Debug.WriteLine("Save completed, product refreshed");
                }
                catch (Exception ex)
                {
                    var errorMessage = GetDetailedErrorMessage(ex);
                    Debug.WriteLine($"Save error: {errorMessage}");
                    ShowTemporaryErrorMessage($"Error saving product: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in save operation: {ex.Message}");
                ShowTemporaryErrorMessage($"Error saving product: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }

        private string GetDetailedErrorMessage(Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append(ex.Message);

            var currentEx = ex;
            while (currentEx.InnerException != null)
            {
                currentEx = currentEx.InnerException;
                sb.Append($"\n→ {currentEx.Message}");
            }

            if (ex is DbUpdateException dbEx && dbEx.Entries != null && dbEx.Entries.Any())
            {
                sb.Append("\nValidation errors:");
                foreach (var entry in dbEx.Entries)
                {
                    sb.Append($"\n- {entry.Entity.GetType().Name}");

                    if (entry.State == EntityState.Added)
                        sb.Append(" (Add)");
                    else if (entry.State == EntityState.Modified)
                        sb.Append(" (Update)");
                    else if (entry.State == EntityState.Deleted)
                        sb.Append(" (Delete)");
                }
            }

            return sb.ToString();
        }

        private async Task GenerateMissingBarcodeImages()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Operation already in progress. Please wait.");
                return;
            }

            try
            {
                IsSaving = true;
                StatusMessage = "Generating missing barcode images...";
                int generatedCount = 0;

                foreach (var product in Products.ToList())
                {
                    if (!string.IsNullOrWhiteSpace(product.Barcode) && product.BarcodeImage == null)
                    {
                        try
                        {
                            var barcodeData = _barcodeService.GenerateBarcode(product.Barcode);
                            if (barcodeData != null)
                            {
                                product.BarcodeImage = barcodeData;

                                var productCopy = new ProductDTO
                                {
                                    ProductId = product.ProductId,
                                    Name = product.Name,
                                    Barcode = product.Barcode,
                                    CategoryId = product.CategoryId,
                                    CategoryName = product.CategoryName,
                                    SupplierId = product.SupplierId,
                                    SupplierName = product.SupplierName,
                                    Description = product.Description,
                                    PurchasePrice = product.PurchasePrice,
                                    SalePrice = product.SalePrice,
                                    CurrentStock = product.CurrentStock,
                                    MinimumStock = product.MinimumStock,
                                    BarcodeImage = product.BarcodeImage,
                                    Speed = product.Speed,
                                    IsActive = product.IsActive,
                                    ImagePath = product.ImagePath,
                                    CreatedAt = product.CreatedAt,
                                    UpdatedAt = DateTime.Now
                                };

                                await _productService.UpdateAsync(productCopy);
                                generatedCount++;

                                if (generatedCount % 5 == 0)
                                {
                                    StatusMessage = $"Generated {generatedCount} barcode images...";
                                    await Task.Delay(10);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error generating barcode for product {product.Name}: {ex.Message}");
                        }
                    }
                }

                StatusMessage = $"Successfully generated {generatedCount} barcode images.";
                await Task.Delay(2000);

                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error generating barcode images: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }

        private async Task RefreshSpecificProduct(int productId)
        {
            try
            {
                var updatedProduct = await _productService.GetByIdAsync(productId);
                if (updatedProduct != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        for (int i = 0; i < Products.Count; i++)
                        {
                            if (Products[i].ProductId == productId)
                            {
                                Products[i] = updatedProduct;
                                Debug.WriteLine($"Refreshed specific product: {updatedProduct.Name}");
                                break;
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing specific product: {ex.Message}");
            }
        }

        private bool ValidateProduct(ProductDTO product)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(product.Name))
                errors.Add("Product name is required");

            if (product.SalePrice <= 0)
                errors.Add("Sale price must be greater than zero");

            if (product.PurchasePrice < 0)
                errors.Add("Purchase price cannot be negative");

            if (product.MinimumStock < 0)
                errors.Add("Minimum stock cannot be negative");

            if (!string.IsNullOrWhiteSpace(product.Speed))
            {
                if (!decimal.TryParse(product.Speed, out _))
                {
                    errors.Add("Speed must be a valid number");
                }
            }

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

        private async Task DeleteAsync()
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
                    return MessageBox.Show($"Are you sure you want to delete product '{SelectedProduct.Name}'?",
                        "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                });

                if (result == MessageBoxResult.Yes)
                {
                    IsSaving = true;
                    StatusMessage = "Deleting product...";

                    var productId = SelectedProduct.ProductId;
                    var productName = SelectedProduct.Name;

                    try
                    {
                        await _productService.DeleteAsync(productId);

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var productToRemove = Products.FirstOrDefault(p => p.ProductId == productId);
                            if (productToRemove != null)
                            {
                                Products.Remove(productToRemove);
                            }
                        });

                        if (IsProductPopupOpen)
                        {
                            CloseProductPopup();
                        }

                        CalculateAggregatedValues();

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show($"Product '{productName}' has been deleted successfully.",
                                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        });

                        SelectedProduct = null;

                        await SafeLoadDataAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error deleting product {productId}: {ex.Message}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error deleting product: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }

        private void GenerateBarcode()
        {
            if (SelectedProduct == null || string.IsNullOrWhiteSpace(SelectedProduct.Barcode))
            {
                ShowTemporaryErrorMessage("Please enter a barcode value first.");
                return;
            }

            try
            {
                var barcode = SelectedProduct.Barcode;
                if (barcode.Length > 12)
                {
                    barcode = barcode.Substring(0, 12);
                    SelectedProduct.Barcode = barcode;
                    ShowTemporaryErrorMessage("Barcode was truncated to 12 digits.");
                }

                var barcodeData = _barcodeService.GenerateBarcode(barcode);
                if (barcodeData != null)
                {
                    SelectedProduct.BarcodeImage = barcodeData;
                    BarcodeImage = LoadBarcodeImage(barcodeData);

                    if (BarcodeImage == null)
                    {
                        ShowTemporaryErrorMessage("Failed to load barcode image.");
                    }
                }
                else
                {
                    ShowTemporaryErrorMessage("Failed to generate barcode.");
                }
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error generating barcode: {ex.Message}");
            }
        }

        private void GenerateAutomaticBarcode()
        {
            if (SelectedProduct == null)
            {
                ShowTemporaryErrorMessage("Please select a product first.");
                return;
            }

            try
            {
                var categoryPrefix = "01";
                var sequential = DateTime.Now.ToString("mmss");
                var random = new Random();
                var randomDigits = random.Next(10, 99).ToString();

                SelectedProduct.Barcode = $"{categoryPrefix}{sequential}{randomDigits}";

                var barcodeData = _barcodeService.GenerateBarcode(SelectedProduct.Barcode);

                if (barcodeData != null)
                {
                    SelectedProduct.BarcodeImage = barcodeData;
                    BarcodeImage = LoadBarcodeImage(barcodeData);

                    if (BarcodeImage == null)
                    {
                        ShowTemporaryErrorMessage("Failed to load barcode image.");
                    }
                }
                else
                {
                    ShowTemporaryErrorMessage("Failed to generate barcode.");
                }
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error generating automatic barcode: {ex.Message}");
            }
        }

        private BitmapImage LoadBarcodeImage(byte[] imageData)
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

                    image.DecodePixelWidth = 600;
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

                    image.EndInit();
                    image.Freeze();
                }
                return image;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading barcode image: {ex.Message}");
                return null;
            }
        }

        private void ShowTemporaryErrorMessage(string message)
        {
            StatusMessage = message;

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
                    if (StatusMessage == message)
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