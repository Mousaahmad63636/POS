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
        // Pagination properties
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalPages;
        private ObservableCollection<int> _pageNumbers;
        private List<int> _visiblePageNumbers = new List<int>();
        private int _totalProducts;

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
                IsEditing = value != null;

                if (value != null)
                {
                    // Subscribe to property changes on the new selected product
                    value.PropertyChanged += SelectedProduct_PropertyChanged;

                    if (value.BarcodeImage != null)
                    {
                        LoadBarcodeImage(value.BarcodeImage);
                    }
                    else
                    {
                        BarcodeImage = null;
                    }

                    // Update to load image from path instead of byte array
                    ProductImage = value.ImagePath != null ? LoadImageFromPath(value.ImagePath) : null;

                    // Calculate selected product values immediately
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
                    // Fallback if service not available
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

                // Properly create a file URI
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;

                // Use the file:// protocol with proper URI creation
                Uri fileUri = new Uri("file:///" + fullPath.Replace('\\', '/'));
                image.UriSource = fileUri;

                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image from path: {ex.Message}");

                // The fallback method has scope issues - let's fix the implementation:
                try
                {
                    // Get the path again since we lost scope
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

                    // Fallback to stream-based loading with correct parameters
                    BitmapImage fallbackImage = new BitmapImage();

                    // Use using statement with proper FileStream parameters
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
            // When price properties or stock change, recalculate values
            if (e.PropertyName == nameof(ProductDTO.PurchasePrice) ||
                e.PropertyName == nameof(ProductDTO.SalePrice) ||
                e.PropertyName == nameof(ProductDTO.CurrentStock))
            {
                CalculateSelectedProductValues();
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

        // Pagination commands
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

            // Pagination commands
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

        // Update these methods in the ProductViewModel.cs file

        public void ShowProductPopup()
        {
            try
            {
                // Create a new instance of the ProductDetailsWindow
                var productWindow = new ProductDetailsWindow
                {
                    DataContext = this,
                    Owner = GetOwnerWindow()
                };

                // Subscribe to save completed event
                productWindow.SaveCompleted += ProductDetailsWindow_SaveCompleted;

                // Show the window as dialog
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
            // When save is completed, close the window
            // The window itself already handles closing through the DialogResult
        }

        public void CloseProductPopup()
        {
            // This is no longer needed with Window approach, as the window handles its own closing
            // But we'll keep it for backward compatibility
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
                // Publish an event to refresh product lists in other viewmodels
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
            // Store current popup state
            bool wasPopupOpen = IsProductPopupOpen;

            // Temporarily close the popup
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

                // Get the current owner window
                var ownerWindow = GetOwnerWindow();

                // Show dialog with proper owner
                bool? result = openFileDialog.ShowDialog(ownerWindow);

                if (result == true && SelectedProduct != null)
                {
                    try
                    {
                        // Get the source path
                        string sourcePath = openFileDialog.FileName;

                        // Save the image and get the relative path
                        string savedPath = _imagePathService.SaveProductImage(sourcePath);

                        // Set the ImagePath property (not the old Image property)
                        SelectedProduct.ImagePath = savedPath;

                        // Use your local method instead of calling the service
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
                // Restore popup state
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
                // If there's an existing image, attempt to delete the file
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

                // Generate barcode image if needed
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

                        // Save updated product with barcode image
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

                // Print the labels in a single document
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

                        // Configure print ticket for label printing
                        printDialog.PrintTicket.PageMediaSize = new PageMediaSize(
                            (int)(5.5 * 39.37), // Width in hundredths of an inch
                            (int)(4.0 * 39.37)  // Height in hundredths of an inch
                        );
                        printDialog.PrintTicket.PageBorderless = PageBorderless.Borderless;
                        printDialog.PrintTicket.PageMediaType = PageMediaType.Label;

                        // Create a single document with multiple pages (one for each label)
                        var fixedDocument = new FixedDocument();

                        StatusMessage = $"Creating document with {LabelsPerProduct} labels...";

                        // Add each label as a separate page in the document
                        for (int i = 0; i < LabelsPerProduct; i++)
                        {
                            var pageContent = new PageContent();
                            var fixedPage = new FixedPage();

                            // Set page dimensions
                            fixedPage.Width = printDialog.PrintableAreaWidth;
                            fixedPage.Height = printDialog.PrintableAreaHeight;

                            // Create visual for this label
                            var labelVisual = CreateBarcodeLabelVisual(SelectedProduct,
                                printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);

                            // Add to page
                            fixedPage.Children.Add(labelVisual);

                            // Add page to document
                            ((IAddChild)pageContent).AddChild(fixedPage);
                            fixedDocument.Pages.Add(pageContent);
                        }

                        // Print the entire document in one operation
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
        private UIElement CreateHighQualityBarcodeLabel(ProductDTO product, double width, double height)
        {
            // Create a container for the label content with top padding
            var outerCanvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = Brushes.White
            };

            // Create inner canvas for content that will be shifted down
            var canvas = new Canvas
            {
                Width = width,
                Height = height - 15
            };

            // Position the inner canvas with top padding to shift everything down
            Canvas.SetTop(canvas, 15);
            outerCanvas.Children.Add(canvas);

            // Position the barcode image - use most of the available space
            double barcodeWidth = Math.Min(width * 0.9, 600);
            double barcodeHeight = Math.Min(height * 0.5, 200);

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

                // Add product name with improved text quality
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

                // High-quality text rendering
                TextOptions.SetTextRenderingMode(nameTextBlock, TextRenderingMode.ClearType);
                TextOptions.SetTextFormattingMode(nameTextBlock, TextFormattingMode.Display);

                Canvas.SetLeft(nameTextBlock, (width - nameTextBlock.Width) / 2);
                Canvas.SetTop(nameTextBlock, 0);
                canvas.Children.Add(nameTextBlock);

                double barcodeTop = height * 0.15;

                BitmapImage bitmapSource = null;
                if (product.BarcodeImage != null)
                {
                    bitmapSource = LoadHighQualityBarcodeImage(product.BarcodeImage);
                }

                if (bitmapSource == null)
                {
                    // Create a placeholder for missing barcode image
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
                    // Create and position barcode image with enhanced quality settings
                    var barcodeImage = new Image
                    {
                        Source = bitmapSource,
                        Width = barcodeWidth,
                        Height = barcodeHeight,
                        Stretch = Stretch.Uniform,
                        SnapsToDevicePixels = true,
                        UseLayoutRounding = true // Ensures pixel-perfect rendering
                    };

                    // Critical: Use NearestNeighbor for barcodes to prevent smoothing
                    RenderOptions.SetBitmapScalingMode(barcodeImage, BitmapScalingMode.NearestNeighbor);
                    RenderOptions.SetEdgeMode(barcodeImage, EdgeMode.Aliased);
                    RenderOptions.SetClearTypeHint(barcodeImage, ClearTypeHint.Enabled);

                    Canvas.SetLeft(barcodeImage, (width - barcodeWidth) / 2);
                    Canvas.SetTop(barcodeImage, barcodeTop);
                    canvas.Children.Add(barcodeImage);
                }

                // Add barcode text
                var barcodeTextBlock = new TextBlock
                {
                    Text = displayBarcode,
                    FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                    FontSize = 9,
                    TextAlignment = TextAlignment.Center,
                    Width = width * 0.9
                };

                TextOptions.SetTextRenderingMode(barcodeTextBlock, TextRenderingMode.ClearType);
                TextOptions.SetTextFormattingMode(barcodeTextBlock, TextFormattingMode.Display);

                double barcodeImageBottom = barcodeTop + barcodeHeight;
                Canvas.SetLeft(barcodeTextBlock, (width - barcodeTextBlock.Width) / 2);
                Canvas.SetTop(barcodeTextBlock, barcodeImageBottom + 5);
                canvas.Children.Add(barcodeTextBlock);

                // Add price if needed
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

                    TextOptions.SetTextRenderingMode(priceTextBlock, TextRenderingMode.ClearType);
                    TextOptions.SetTextFormattingMode(priceTextBlock, TextFormattingMode.Display);

                    Canvas.SetLeft(priceTextBlock, (width - priceTextBlock.Width) / 2);
                    Canvas.SetTop(priceTextBlock, height * 0.75);
                    canvas.Children.Add(priceTextBlock);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating high-quality barcode label: {ex.Message}");

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
        private BitmapImage LoadHighQualityBarcodeImage(byte[] imageData)
        {
            if (imageData == null) return null;

            var image = new BitmapImage();
            try
            {
                using (var ms = new MemoryStream(imageData))
                {
                    image.BeginInit();

                    // High quality loading settings
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat |
                                          BitmapCreateOptions.IgnoreImageCache |
                                          BitmapCreateOptions.IgnoreColorProfile;

                    // Load at full resolution - don't decode at a different size
                    // This preserves the sharp edges of the barcode

                    image.StreamSource = ms;
                    image.EndInit();
                    image.Freeze(); // Important for cross-thread usage
                }
                return image;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading high-quality barcode image: {ex.Message}");
                return null;
            }
        }
        private UIElement CreateBarcodeLabelVisual(ProductDTO product, double width, double height)
        {
            // Create a container for the label content with top padding
            var outerCanvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = Brushes.White
            };

            // Create inner canvas for content that will be shifted down
            var canvas = new Canvas
            {
                Width = width,
                // Use slightly reduced height to accommodate the top padding
                Height = height - 15
            };

            // Position the inner canvas with top padding to shift everything down
            Canvas.SetTop(canvas, 15); // Add 15 pixels of top padding
            outerCanvas.Children.Add(canvas);

            // Position the barcode image - use most of the available space
            double barcodeWidth = width * 0.9;
            double barcodeHeight = height * 0.5;

            try
            {
                // Check if product is null
                if (product == null)
                {
                    throw new ArgumentNullException("product", "Product cannot be null");
                }

                // Verify barcode is within limits (12 digits max)
                string displayBarcode = product.Barcode ?? "N/A";
                if (!string.IsNullOrEmpty(displayBarcode) && displayBarcode.Length > 12)
                {
                    Debug.WriteLine($"Warning: Barcode '{displayBarcode}' exceeds 12 digits. It may not scan correctly.");
                }

                // Add product name (with null check)
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

                // Position product name at top of inner canvas
                Canvas.SetLeft(nameTextBlock, (width - nameTextBlock.Width) / 2);
                Canvas.SetTop(nameTextBlock, 0); // Position at top of inner canvas
                canvas.Children.Add(nameTextBlock);

                // Standard position for barcode relative to inner canvas
                double barcodeTop = height * 0.15;

                // Load barcode image with null check
                BitmapImage bitmapSource = null;
                if (product.BarcodeImage != null)
                {
                    bitmapSource = LoadBarcodeImage(product.BarcodeImage);
                }

                // Handle case where image didn't load
                if (bitmapSource == null)
                {
                    // Create a placeholder for missing barcode image
                    var placeholder = new Border
                    {
                        Width = barcodeWidth,
                        Height = barcodeHeight,
                        Background = Brushes.LightGray,
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(1)
                    };

                    // Add text to placeholder
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

                    // Position placeholder
                    Canvas.SetLeft(placeholder, (width - barcodeWidth) / 2);
                    Canvas.SetTop(placeholder, barcodeTop);
                    canvas.Children.Add(placeholder);
                }
                else
                {
                    // Create and position barcode image with high-quality rendering
                    var barcodeImage = new Image
                    {
                        Source = bitmapSource,
                        Width = barcodeWidth,
                        Height = barcodeHeight,
                        Stretch = Stretch.Uniform,
                        SnapsToDevicePixels = true
                    };

                    // Set high-quality rendering options
                    RenderOptions.SetBitmapScalingMode(barcodeImage, BitmapScalingMode.HighQuality);
                    RenderOptions.SetEdgeMode(barcodeImage, EdgeMode.Aliased);

                    Canvas.SetLeft(barcodeImage, (width - barcodeWidth) / 2);
                    Canvas.SetTop(barcodeImage, barcodeTop);
                    canvas.Children.Add(barcodeImage);
                }

                // Add barcode text (with null check)
                var barcodeTextBlock = new TextBlock
                {
                    Text = displayBarcode,
                    FontFamily = new FontFamily("Consolas, Courier New, Monospace"), // Monospace is better for barcodes
                    FontSize = 9,
                    TextAlignment = TextAlignment.Center,
                    Width = width * 0.9
                };

                // Position barcode text below where the barcode image would be
                double barcodeImageBottom = barcodeTop + barcodeHeight;
                Canvas.SetLeft(barcodeTextBlock, (width - barcodeTextBlock.Width) / 2);
                Canvas.SetTop(barcodeTextBlock, barcodeImageBottom + 5);
                canvas.Children.Add(barcodeTextBlock);

                // Add price if needed
                if (product.SalePrice > 0)
                {
                    var priceTextBlock = new TextBlock
                    {
                        Text = $"${product.SalePrice:N2}", // Only show price without duplicating barcode
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        Width = width * 0.9
                    };

                    // Position price at bottom with better spacing
                    Canvas.SetLeft(priceTextBlock, (width - priceTextBlock.Width) / 2);
                    Canvas.SetTop(priceTextBlock, height * 0.75); // Adjusted for top padding
                    canvas.Children.Add(priceTextBlock);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating barcode visual: {ex.Message}");

                // Add error message if there's an exception
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
        private FixedDocument CreateBarcodeDocument(ProductDTO product, int numberOfLabels)
        {
            var document = new FixedDocument();
            var pageSize = new Size(96 * 8.5, 96 * 11); // Letter size at 96 DPI
            var labelSize = new Size(96 * 2, 96); // 2 inches x 1 inch at 96 DPI
            var margin = new Thickness(96 * 0.5); // 0.5 inch margins

            var labelsPerRow = (int)((pageSize.Width - margin.Left - margin.Right) / labelSize.Width);
            var labelsPerColumn = (int)((pageSize.Height - margin.Top - margin.Bottom) / labelSize.Height);
            var labelsPerPage = labelsPerRow * labelsPerColumn;

            var currentPage = CreateNewPage(pageSize, margin);
            var currentPanel = (WrapPanel)((FixedPage)currentPage.Child).Children[0];
            var currentLabelCount = 0;

            for (int i = 0; i < numberOfLabels; i++)
            {
                if (currentLabelCount >= labelsPerPage)
                {
                    document.Pages.Add(currentPage);
                    currentPage = CreateNewPage(pageSize, margin);
                    currentPanel = (WrapPanel)((FixedPage)currentPage.Child).Children[0];
                    currentLabelCount = 0;
                }

                var label = CreateBarcodeLabel(product, labelSize);
                currentPanel.Children.Add(label);
                currentLabelCount++;
            }

            if (currentLabelCount > 0)
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
            // Create outer grid with top padding
            var outerGrid = new Grid
            {
                Width = labelSize.Width,
                Height = labelSize.Height,
                Margin = new Thickness(2),
                Background = Brushes.White
            };

            // Define rows for the outer grid
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Top padding
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content area

            // Create inner grid for actual content
            var contentGrid = new Grid
            {
                Width = labelSize.Width,
                VerticalAlignment = VerticalAlignment.Top
            };

            // Set the inner grid to be in the second row (after top padding)
            Grid.SetRow(contentGrid, 1);
            outerGrid.Children.Add(contentGrid);

            // Define rows for the content grid
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });  // Product name
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });  // Barcode image
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // Barcode text
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // Price (optional)

            // Add product name
            var nameText = new TextBlock
            {
                Text = product.Name ?? "Unknown Product",
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2),
                FontWeight = FontWeights.Bold,
                FontSize = 10
            };
            Grid.SetRow(nameText, 0);
            contentGrid.Children.Add(nameText);

            // Create and add barcode image
            BitmapImage bitmapSource = null;
            if (product.BarcodeImage != null)
            {
                bitmapSource = LoadBarcodeImage(product.BarcodeImage);
            }

            var image = new Image
            {
                Source = bitmapSource,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(5)
            };

            // Set high-quality settings
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

            Grid.SetRow(image, 1);
            contentGrid.Children.Add(image);

            // Add barcode text
            var barcodeText = new TextBlock
            {
                Text = product.Barcode ?? "No Barcode",
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 5),
                FontFamily = new FontFamily("Consolas, Courier New, Monospace"), // Monospace for barcode text
                FontSize = 9
            };
            Grid.SetRow(barcodeText, 2);
            contentGrid.Children.Add(barcodeText);

            // Add price (if available)
            if (product.SalePrice > 0)
            {
                var priceText = new TextBlock
                {
                    Text = $"${product.SalePrice:N2}",
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 2),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12
                };
                Grid.SetRow(priceText, 3);
                contentGrid.Children.Add(priceText);
            }

            return outerGrid;
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

                // Get the current owner window
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
                            // Only add if the supplier is active
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
                                    // Update the existing supplier if it's active
                                    Suppliers[existingIndex] = evt.Entity;
                                    Debug.WriteLine($"Updated supplier {evt.Entity.Name}");
                                }
                                else
                                {
                                    // Remove the supplier if it's now inactive
                                    Suppliers.RemoveAt(existingIndex);
                                    Debug.WriteLine($"Removed inactive supplier {evt.Entity.Name}");
                                }
                            }
                            else if (evt.Entity.IsActive)
                            {
                                // This is a supplier that wasn't in our list but is now active
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

                    // Wait for categories and suppliers to complete
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

                // Store reference to product before any operations that might clear it
                var productToUpdate = SelectedProduct;

                // Check if barcode is empty and generate one if needed
                if (string.IsNullOrWhiteSpace(productToUpdate.Barcode))
                {
                    Debug.WriteLine("No barcode provided, generating automatic barcode");

                    // Generate a unique barcode based on category, timestamp, and random number
                    var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8); // Use ticks for uniqueness
                    var random = new Random();
                    var randomDigits = random.Next(1000, 9999).ToString();
                    var categoryPrefix = productToUpdate.CategoryId.ToString().PadLeft(3, '0');

                    productToUpdate.Barcode = $"{categoryPrefix}-{timestamp}-{randomDigits}";
                }

                // Always ensure barcode image exists before saving
                if (productToUpdate.BarcodeImage == null && !string.IsNullOrWhiteSpace(productToUpdate.Barcode))
                {
                    Debug.WriteLine("Generating barcode image for product");
                    try
                    {
                        productToUpdate.BarcodeImage = _barcodeService.GenerateBarcode(productToUpdate.Barcode);

                        // Update the UI image
                        BarcodeImage = LoadBarcodeImage(productToUpdate.BarcodeImage);
                        Debug.WriteLine("Barcode image generated successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                        // Continue despite this error - we can still save without the image
                    }
                }

                if (!ValidateProduct(productToUpdate))
                {
                    return;
                }

                // Check for duplicate barcode
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
                    // Continue despite this error, as it's better to attempt the save
                }

                StatusMessage = "Saving product...";

                // Create a copy of the product to ensure we have the latest values
                var productCopy = new ProductDTO
                {
                    ProductId = productToUpdate.ProductId,
                    Name = productToUpdate.Name,
                    Barcode = productToUpdate.Barcode,
                    CategoryId = productToUpdate.CategoryId,
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
                    ImagePath = productToUpdate.ImagePath, // Changed from Image to ImagePath
                    CreatedAt = productToUpdate.CreatedAt,
                    UpdatedAt = DateTime.Now
                };

                try
                {
                    if (productToUpdate.ProductId == 0)
                    {
                        var result = await _productService.CreateAsync(productCopy);

                        // Update SelectedProduct reference
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                            SelectedProduct = result;
                        });

                        // Use the result for updating UI
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

                    // Recalculate totals after saving
                    CalculateAggregatedValues();
                    CalculateSelectedProductValues();

                    CloseProductPopup();

                    // Refresh specific product
                    await RefreshSpecificProduct(productToUpdate.ProductId);

                    // NEW LINE: Explicitly publish the event for the new product to update TransactionViewModel
                    await RefreshTransactionProductLists();

                    // Refresh the data
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

            // Collect inner exception details
            var currentEx = ex;
            while (currentEx.InnerException != null)
            {
                currentEx = currentEx.InnerException;
                sb.Append($"\n→ {currentEx.Message}");
            }

            // Add Entity Framework validation errors if available
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

                                // Update status message periodically
                                if (generatedCount % 5 == 0)
                                {
                                    StatusMessage = $"Generated {generatedCount} barcode images...";
                                    await Task.Delay(10); // Allow UI to update
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error generating barcode for product {product.Name}: {ex.Message}");
                            // Continue with next product
                        }
                    }
                }

                StatusMessage = $"Successfully generated {generatedCount} barcode images.";
                await Task.Delay(2000);

                // Refresh products to ensure we have the latest data
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
                        // Find and update the product in the collection
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

            if (product.CategoryId <= 0)
                errors.Add("Please select a category");

            if (product.SalePrice <= 0)
                errors.Add("Sale price must be greater than zero");

            // Modified validation: allows purchase price of 0 but prevents negative values
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

                        // Remove from the local collection
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var productToRemove = Products.FirstOrDefault(p => p.ProductId == productId);
                            if (productToRemove != null)
                            {
                                Products.Remove(productToRemove);
                            }
                        });

                        // Close popup if it's open
                        if (IsProductPopupOpen)
                        {
                            CloseProductPopup();
                        }

                        // Recalculate totals after deletion
                        CalculateAggregatedValues();

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show($"Product '{productName}' has been deleted successfully.",
                                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        });

                        // Clear the selected product
                        SelectedProduct = null;

                        // Refresh the data
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
                // Ensure barcode is within 12 digits
                var barcode = SelectedProduct.Barcode;
                if (barcode.Length > 12)
                {
                    // Truncate to 12 digits if longer
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
                // Format: [Category(2)][Sequential(4)][Random(2)] = Total 8 digits
                // This ensures we stay well below the 12 digit limit

                // Category prefix (2 digits max)
                var categoryPrefix = (SelectedProduct.CategoryId % 100).ToString().PadLeft(2, '0');

                // Sequential number (4 digits)
                var sequential = DateTime.Now.ToString("mmss");

                // Random digits (2 digits)
                var random = new Random();
                var randomDigits = random.Next(10, 99).ToString();

                // Combine to create a unique barcode (8 digits total)
                SelectedProduct.Barcode = $"{categoryPrefix}{sequential}{randomDigits}";

                // Generate barcode image
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

                    // Add these lines for higher quality
                    image.DecodePixelWidth = 600; // Higher resolution decoding
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

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