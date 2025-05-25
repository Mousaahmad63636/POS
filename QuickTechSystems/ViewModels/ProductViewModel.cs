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
using QuickTechSystems.Application.Interfaces;
using System.IO;
using System.Windows.Documents;
using System.Printing;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Controls;

namespace QuickTechSystems.WPF.ViewModels
{
    public class ProductViewModel : ViewModelBase
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IMainStockService _mainStockService;
        private readonly IDbContextScopeService _dbContextScopeService;
        private readonly IBarcodeService _barcodeService;
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
        private int _totalBoxes;
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

        public void RefreshProductCalculations(ProductDTO product)
        {
            if (product == null) return;

            // Recalculate totals for the specific product
            decimal totalCost = product.PurchasePrice * product.CurrentStock;
            decimal totalValue = product.SalePrice * product.CurrentStock;

            // If this is the selected product, update the calculations
            if (SelectedProduct != null && SelectedProduct.ProductId == product.ProductId)
            {
                CalculateSelectedProductValues();
            }

            // Update the global calculations
            CalculateAggregatedValues();
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

        public int TotalBoxes
        {
            get => _totalBoxes;
            set => SetProperty(ref _totalBoxes, value);
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
        public ICommand SyncWithMainStockCommand { get; private set; }

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
            IDbContextScopeService dbContextScopeService,
            IBarcodeService barcodeService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            Debug.WriteLine("Initializing ProductViewModel");
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _mainStockService = mainStockService ?? throw new ArgumentNullException(nameof(mainStockService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));
            _dbContextScopeService = dbContextScopeService ?? throw new ArgumentNullException(nameof(dbContextScopeService));
            _barcodeService = barcodeService ?? throw new ArgumentNullException(nameof(barcodeService));

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

            // Test barcode generation on startup (debug mode only)
#if DEBUG
            try
            {
                TestBarcodeGeneration();
            }
            catch (Exception testEx)
            {
                Debug.WriteLine($"Barcode test failed during initialization: {testEx.Message}");
            }
#endif

            _ = LoadDataAsync();
            Debug.WriteLine("ProductViewModel initialized");
        }

        private void InitializeCommands()
        {
            LoadCommand = new AsyncRelayCommand(async _ => await LoadDataAsync(), _ => !IsSaving);
            UpdateStockCommand = new AsyncRelayCommand(async _ => await UpdateStockAsync(), _ => !IsSaving);
            PrintBarcodeCommand = new AsyncRelayCommand(async _ => await PrintBarcodeAsync(), _ => !IsSaving);
            SyncWithMainStockCommand = new AsyncRelayCommand(async _ => await SyncWithMainStockAsync(), _ => !IsSaving);

            // Pagination commands
            NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => !IsLastPage);
            PreviousPageCommand = new RelayCommand(_ => CurrentPage--, _ => !IsFirstPage);
            GoToPageCommand = new RelayCommand<int>(page => CurrentPage = page);
            ChangePageSizeCommand = new RelayCommand<int>(size => PageSize = size);
        }

        // Enhanced Barcode Printing Method
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

                StatusMessage = "Preparing barcode labels...";
                IsSaving = true;

                // Test barcode generation first to catch any errors early
                try
                {
                    var testBarcodeData = _barcodeService.GenerateBarcode(SelectedProduct.Barcode, 300, 100);
                    if (testBarcodeData == null)
                    {
                        ShowTemporaryErrorMessage("Failed to generate barcode. Please check the barcode format.");
                        return;
                    }

                    // Test bitmap loading
                    var testBitmap = LoadBarcodeImage(testBarcodeData);
                    if (testBitmap == null)
                    {
                        ShowTemporaryErrorMessage("Failed to load barcode image. There may be an issue with the barcode format.");
                        return;
                    }

                    Debug.WriteLine($"Pre-print test successful: barcode generated and loaded");
                }
                catch (Exception testEx)
                {
                    ShowTemporaryErrorMessage($"Error generating barcode: {testEx.Message}");
                    return;
                }

                // Print the labels
                bool printerCancelled = false;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        StatusMessage = "Opening print dialog...";

                        var printDialog = new PrintDialog();
                        if (printDialog.ShowDialog() != true)
                        {
                            printerCancelled = true;
                            return;
                        }

                        // Get print queue info for debugging
                        Debug.WriteLine($"Selected printer: {printDialog.PrintQueue?.FullName}");
                        Debug.WriteLine($"Printable area: {printDialog.PrintableAreaWidth} x {printDialog.PrintableAreaHeight}");

                        // Configure print ticket with error handling
                        try
                        {
                            if (printDialog.PrintTicket != null)
                            {
                                // Set basic properties with fallbacks
                                printDialog.PrintTicket.PageMediaSize = new PageMediaSize(
                                    PageMediaSizeName.NorthAmericaLetter);

                                // Try to set label-specific properties
                                try
                                {
                                    printDialog.PrintTicket.PageMediaType = PageMediaType.Label;
                                }
                                catch (Exception mediaEx)
                                {
                                    Debug.WriteLine($"Could not set media type to Label: {mediaEx.Message}");
                                }
                            }
                        }
                        catch (Exception configEx)
                        {
                            Debug.WriteLine($"Warning: Could not configure print ticket: {configEx.Message}");
                            // Continue with default settings
                        }

                        StatusMessage = $"Creating document with {LabelsPerProduct} labels...";

                        // Create document using simpler approach
                        var fixedDocument = CreateBarcodeDocument(SelectedProduct, LabelsPerProduct,
                            printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);

                        if (fixedDocument == null || fixedDocument.Pages.Count == 0)
                        {
                            ShowTemporaryErrorMessage("Failed to create printable document.");
                            return;
                        }

                        // Print with error handling
                        StatusMessage = "Sending to printer...";

                        try
                        {
                            printDialog.PrintDocument(fixedDocument.DocumentPaginator,
                                $"Barcode Labels - {SelectedProduct.Name}");

                            StatusMessage = "Barcode labels sent to printer successfully.";
                            Debug.WriteLine($"Successfully sent {fixedDocument.Pages.Count} pages to printer");
                        }
                        catch (System.Printing.PrintingCanceledException)
                        {
                            StatusMessage = "Printing was cancelled.";
                            Debug.WriteLine("Printing cancelled by user or system");
                        }
                        catch (System.Runtime.InteropServices.COMException comEx)
                        {
                            Debug.WriteLine($"COM Exception during printing: {comEx.Message}");
                            ShowTemporaryErrorMessage($"Printer communication error: {comEx.Message}");
                        }
                        catch (Exception printEx)
                        {
                            Debug.WriteLine($"Printing error: {printEx.Message}");
                            ShowTemporaryErrorMessage($"Printing failed: {printEx.Message}");
                        }

                        await Task.Delay(2000);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in print dialog handling: {ex.Message}");
                        ShowTemporaryErrorMessage($"Error preparing print job: {ex.Message}");
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

        private FixedDocument CreateBarcodeDocument(ProductDTO product, int labelCount, double pageWidth, double pageHeight)
        {
            try
            {
                var document = new FixedDocument();

                // Use standard page size if provided dimensions seem invalid
                if (pageWidth <= 0 || pageHeight <= 0)
                {
                    pageWidth = 96 * 8.5;  // 8.5 inches at 96 DPI
                    pageHeight = 96 * 11;  // 11 inches at 96 DPI
                    Debug.WriteLine($"Using default page size: {pageWidth} x {pageHeight}");
                }
                else
                {
                    Debug.WriteLine($"Using printer page size: {pageWidth} x {pageHeight}");
                }

                for (int i = 0; i < labelCount; i++)
                {
                    try
                    {
                        var pageContent = new PageContent();
                        var fixedPage = new FixedPage
                        {
                            Width = pageWidth,
                            Height = pageHeight
                        };

                        // Create the label visual
                        var labelVisual = CreateSimpleBarcodeLabel(product, pageWidth, pageHeight);
                        if (labelVisual != null)
                        {
                            fixedPage.Children.Add(labelVisual);
                        }

                        // Add page to document using proper method
                        ((IAddChild)pageContent).AddChild(fixedPage);
                        document.Pages.Add(pageContent);

                        Debug.WriteLine($"Created page {i + 1} of {labelCount}");
                    }
                    catch (Exception pageEx)
                    {
                        Debug.WriteLine($"Error creating page {i + 1}: {pageEx.Message}");
                        // Continue with other pages
                    }
                }

                Debug.WriteLine($"Document created with {document.Pages.Count} pages");
                return document;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating barcode document: {ex.Message}");
                return null;
            }
        }

        private UIElement CreateSimpleBarcodeLabel(ProductDTO product, double pageWidth, double pageHeight)
        {
            try
            {
                // Create a simple grid-based layout
                var grid = new Grid
                {
                    Width = pageWidth,
                    Height = pageHeight,
                    Background = Brushes.White,
                    Margin = new Thickness(20) // Add margins
                };

                // Define rows
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Product name
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) }); // Barcode image
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Barcode text
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Price

                // Product name
                var nameTextBlock = new TextBlock
                {
                    Text = product.Name ?? "Unknown Product",
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5)
                };
                Grid.SetRow(nameTextBlock, 0);
                grid.Children.Add(nameTextBlock);

                // Generate and add barcode image
                if (!string.IsNullOrWhiteSpace(product.Barcode))
                {
                    try
                    {
                        var barcodeBytes = _barcodeService.GenerateBarcode(product.Barcode, 400, 150);
                        if (barcodeBytes != null)
                        {
                            var barcodeImage = LoadBarcodeImage(barcodeBytes);
                            if (barcodeImage != null)
                            {
                                var imageControl = new Image
                                {
                                    Source = barcodeImage,
                                    Stretch = Stretch.Uniform,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    Margin = new Thickness(10)
                                };

                                // Set rendering options for sharp barcodes
                                RenderOptions.SetBitmapScalingMode(imageControl, BitmapScalingMode.NearestNeighbor);
                                RenderOptions.SetEdgeMode(imageControl, EdgeMode.Aliased);

                                Grid.SetRow(imageControl, 1);
                                grid.Children.Add(imageControl);
                            }
                            else
                            {
                                // Add placeholder if image loading fails
                                AddBarcodePlaceholder(grid, 1);
                            }
                        }
                        else
                        {
                            AddBarcodePlaceholder(grid, 1);
                        }
                    }
                    catch (Exception barcodeEx)
                    {
                        Debug.WriteLine($"Error generating barcode for label: {barcodeEx.Message}");
                        AddBarcodePlaceholder(grid, 1);
                    }
                }
                else
                {
                    AddBarcodePlaceholder(grid, 1);
                }

                // Barcode text
                var barcodeTextBlock = new TextBlock
                {
                    Text = product.Barcode ?? "No Barcode",
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5)
                };
                Grid.SetRow(barcodeTextBlock, 2);
                grid.Children.Add(barcodeTextBlock);

                // Price
                if (product.SalePrice > 0)
                {
                    var priceTextBlock = new TextBlock
                    {
                        Text = $"${product.SalePrice:F2}",
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5)
                    };
                    Grid.SetRow(priceTextBlock, 3);
                    grid.Children.Add(priceTextBlock);
                }

                return grid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating simple barcode label: {ex.Message}");

                // Return a basic error label
                return new TextBlock
                {
                    Text = $"Error creating label for {product.Name}\n{ex.Message}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.Red,
                    Margin = new Thickness(20)
                };
            }
        }

        private void AddBarcodePlaceholder(Grid grid, int row)
        {
            var placeholder = new Border
            {
                Background = Brushes.LightGray,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };

            var placeholderText = new TextBlock
            {
                Text = "Barcode\nNot Available",
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20)
            };

            placeholder.Child = placeholderText;
            Grid.SetRow(placeholder, row);
            grid.Children.Add(placeholder);
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

                // Generate barcode image on-demand from the barcode string
                BitmapImage bitmapSource = null;
                if (!string.IsNullOrWhiteSpace(displayBarcode) && displayBarcode != "N/A")
                {
                    try
                    {
                        // Generate barcode directly from string during printing
                        var barcodeBytes = _barcodeService.GenerateBarcode(displayBarcode, (int)barcodeWidth, (int)barcodeHeight);
                        if (barcodeBytes != null)
                        {
                            bitmapSource = LoadBarcodeImage(barcodeBytes);
                        }
                    }
                    catch (Exception barcodeEx)
                    {
                        Debug.WriteLine($"Error generating barcode for printing: {barcodeEx.Message}");
                    }
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
                        Text = string.IsNullOrWhiteSpace(displayBarcode) || displayBarcode == "N/A"
                            ? "No Barcode\nAvailable"
                            : "Barcode Generation\nFailed",
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
                Debug.WriteLine($"Error creating barcode label: {ex.Message}");

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

        private BitmapImage LoadBarcodeImage(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
            {
                Debug.WriteLine("LoadBarcodeImage: No image data provided");
                return null;
            }

            try
            {
                var image = new BitmapImage();
                using (var ms = new MemoryStream(imageData))
                {
                    ms.Position = 0; // Ensure we're at the beginning
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                    image.Freeze();
                }

                Debug.WriteLine($"LoadBarcodeImage: Successfully loaded barcode image {image.PixelWidth}x{image.PixelHeight}");
                return image;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadBarcodeImage: Error loading barcode image: {ex.Message}");

                // Try alternative loading method
                try
                {
                    Debug.WriteLine("LoadBarcodeImage: Trying alternative loading method...");
                    var image = new BitmapImage();
                    using (var ms = new MemoryStream(imageData))
                    {
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.CreateOptions = BitmapCreateOptions.None;
                        image.StreamSource = ms;
                        image.EndInit();
                    }
                    image.Freeze();
                    Debug.WriteLine($"LoadBarcodeImage: Alternative method succeeded {image.PixelWidth}x{image.PixelHeight}");
                    return image;
                }
                catch (Exception altEx)
                {
                    Debug.WriteLine($"LoadBarcodeImage: Alternative method also failed: {altEx.Message}");
                    return null;
                }
            }
        }

        // Test method for debugging barcode generation
        private void TestBarcodeGeneration(string testBarcode = "123456789012")
        {
            try
            {
                Debug.WriteLine($"Testing barcode generation with: {testBarcode}");

                var barcodeBytes = _barcodeService.GenerateBarcode(testBarcode, 300, 100);

                if (barcodeBytes == null)
                {
                    Debug.WriteLine("ERROR: Barcode generation returned null");
                    return;
                }

                Debug.WriteLine($"SUCCESS: Generated barcode with {barcodeBytes.Length} bytes");

                var bitmapImage = LoadBarcodeImage(barcodeBytes);
                if (bitmapImage != null)
                {
                    Debug.WriteLine($"SUCCESS: Loaded bitmap image {bitmapImage.PixelWidth}x{bitmapImage.PixelHeight}");
                }
                else
                {
                    Debug.WriteLine("ERROR: Failed to load bitmap image from bytes");
                }

                // Test with a shorter barcode as well
                if (testBarcode.Length > 12)
                {
                    var shortBarcode = "123456789";
                    Debug.WriteLine($"Testing with shorter barcode: {shortBarcode}");
                    var shortBarcodeBytes = _barcodeService.GenerateBarcode(shortBarcode, 300, 100);
                    if (shortBarcodeBytes != null)
                    {
                        Debug.WriteLine($"SUCCESS: Short barcode generated with {shortBarcodeBytes.Length} bytes");
                        var shortBitmapImage = LoadBarcodeImage(shortBarcodeBytes);
                        if (shortBitmapImage != null)
                        {
                            Debug.WriteLine($"SUCCESS: Short barcode loaded as {shortBitmapImage.PixelWidth}x{shortBitmapImage.PixelHeight}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in TestBarcodeGeneration: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        // Rest of the existing methods (keeping all the current functionality)
        protected override void SubscribeToEvents()
        {
            Debug.WriteLine("ProductViewModel: Subscribing to events");
            _eventAggregator.Subscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<MainStockDTO>>(_mainStockChangedHandler);
            _eventAggregator.Subscribe<ProductStockUpdatedEvent>(_productStockUpdatedHandler);
            _eventAggregator.Subscribe<GlobalDataRefreshEvent>(HandleGlobalRefresh);
            Debug.WriteLine("ProductViewModel: Subscribed to all events");
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<MainStockDTO>>(_mainStockChangedHandler);
            _eventAggregator.Unsubscribe<ProductStockUpdatedEvent>(_productStockUpdatedHandler);
            _eventAggregator.Unsubscribe<GlobalDataRefreshEvent>(HandleGlobalRefresh);
        }

        private async void HandleGlobalRefresh(GlobalDataRefreshEvent evt)
        {
            Debug.WriteLine($"ProductViewModel: Received global refresh event at {evt.Timestamp}");
            await Task.Delay(800);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _currentPage = 1;
                OnPropertyChanged(nameof(CurrentPage));
            });
            await ForceRefreshDataAsync();
        }

        private async void HandleProductStockUpdated(ProductStockUpdatedEvent evt)
        {
            Debug.WriteLine($"ProductViewModel: Product stock updated - ID: {evt.ProductId}, New Stock: {evt.NewStock}");
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var product = Products.FirstOrDefault(p => p.ProductId == evt.ProductId);
                    if (product != null)
                    {
                        product.CurrentStock = (int)Math.Round(evt.NewStock);
                        Debug.WriteLine($"ProductViewModel: Updated product {product.Name} stock to {product.CurrentStock}");

                        if (SelectedProduct != null && SelectedProduct.ProductId == evt.ProductId)
                        {
                            SelectedProduct.CurrentStock = (int)Math.Round(evt.NewStock);
                            CalculateSelectedProductValues();
                        }
                        CalculateAggregatedValues();
                    }
                    else
                    {
                        Debug.WriteLine("ProductViewModel: Product not found in current page, forcing reload");
                        Task.Run(async () => await ForceRefreshDataAsync());
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
            if (e.PropertyName == nameof(ProductDTO.PurchasePrice) ||
                e.PropertyName == nameof(ProductDTO.SalePrice) ||
                e.PropertyName == nameof(ProductDTO.CurrentStock))
            {
                CalculateSelectedProductValues();
            }
        }

        private async void HandleMainStockChanged(EntityChangedEvent<MainStockDTO> evt)
        {
            try
            {
                Debug.WriteLine($"ProductViewModel: Handling MainStock change {evt.Action} for ID {evt.Entity.MainStockId}");
                var linkedProducts = Products.Where(p => p.MainStockId.HasValue && p.MainStockId.Value == evt.Entity.MainStockId).ToList();

                if (linkedProducts.Any())
                {
                    bool valuesChanged = false;
                    foreach (var product in linkedProducts)
                    {
                        if (Math.Abs(product.PurchasePrice - evt.Entity.PurchasePrice) > 0.001m)
                        {
                            product.PurchasePrice = evt.Entity.PurchasePrice;
                            valuesChanged = true;
                        }
                        if (Math.Abs(product.SalePrice - evt.Entity.SalePrice) > 0.001m)
                        {
                            product.SalePrice = evt.Entity.SalePrice;
                            valuesChanged = true;
                        }
                        if (Math.Abs(product.BoxPurchasePrice - evt.Entity.BoxPurchasePrice) > 0.001m)
                        {
                            product.BoxPurchasePrice = evt.Entity.BoxPurchasePrice;
                            valuesChanged = true;
                        }
                        if (Math.Abs(product.BoxSalePrice - evt.Entity.BoxSalePrice) > 0.001m)
                        {
                            product.BoxSalePrice = evt.Entity.BoxSalePrice;
                            valuesChanged = true;
                        }
                        if (product.ItemsPerBox != evt.Entity.ItemsPerBox)
                        {
                            product.ItemsPerBox = evt.Entity.ItemsPerBox;
                            valuesChanged = true;
                        }
                    }

                    if (valuesChanged)
                    {
                        CalculateAggregatedValues();
                        if (SelectedProduct != null && linkedProducts.Any(p => p.ProductId == SelectedProduct.ProductId))
                        {
                            CalculateSelectedProductValues();
                        }
                        await Task.Delay(500);
                        await ForceRefreshDataAsync();
                    }
                }
                else
                {
                    await ForceRefreshDataAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProductViewModel: Error handling MainStock change: {ex.Message}");
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
                            }
                            else if (evt.Entity.IsActive)
                            {
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
                Debug.WriteLine($"Handling {evt.Action} event for product {evt.Entity.Name} (ID: {evt.Entity.ProductId})");
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
                                if (existingProduct.CurrentStock > 0 && evt.Entity.CurrentStock == 0)
                                {
                                    evt.Entity.CurrentStock = existingProduct.CurrentStock;
                                }
                                Products[index] = evt.Entity;
                                Debug.WriteLine("Product updated in collection");
                            }
                            else
                            {
                                if (string.IsNullOrWhiteSpace(SearchText) ||
                                    evt.Entity.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                    evt.Entity.Barcode.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                    evt.Entity.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                                {
                                    Task.Run(async () => await ForceRefreshDataAsync());
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
                    TotalBoxes = 0;
                    return;
                }

                decimal totalPurchase = 0;
                decimal totalSale = 0;
                int totalBoxes = 0;

                foreach (var product in Products)
                {
                    try
                    {
                        totalPurchase += Math.Round(product.PurchasePrice * product.CurrentStock, 2);
                        totalSale += Math.Round(product.SalePrice * product.CurrentStock, 2);
                        totalBoxes += product.NumberOfBoxes;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error calculating values for product {product.Name}: {ex.Message}");
                    }
                }

                TotalPurchaseValue = totalPurchase;
                TotalSaleValue = totalSale;
                TotalProfit = totalSale - totalPurchase;
                TotalBoxes = totalBoxes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating aggregated values: {ex.Message}");
                TotalPurchaseValue = 0;
                TotalSaleValue = 0;
                TotalProfit = 0;
                TotalBoxes = 0;
            }
        }

        public async Task ForceRefreshDataAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                Debug.WriteLine("ProductViewModel: ForceRefreshDataAsync - another refresh operation is in progress");
                return;
            }

            try
            {
                Debug.WriteLine("ProductViewModel: Forcing complete data refresh");
                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                bool success = false;
                int maxRetries = 3;

                for (int attempt = 0; attempt < maxRetries && !success; attempt++)
                {
                    try
                    {
                        Debug.WriteLine($"ForceRefreshDataAsync attempt {attempt + 1}");
                        await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                        {
                            IsSaving = true;
                            StatusMessage = "Refreshing data...";

                            var products = await _productService.GetAllAsync();
                            var categories = await _categoryService.GetActiveAsync();
                            var suppliers = await _supplierService.GetActiveAsync();
                            var mainStocks = await _mainStockService.GetAllAsync();

                            await Task.Delay(100);

                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                var pagedProducts = products
                                    .Skip((CurrentPage - 1) * PageSize)
                                    .Take(PageSize)
                                    .ToList();

                                Products = new ObservableCollection<ProductDTO>(pagedProducts);
                                Categories = new ObservableCollection<CategoryDTO>(categories);
                                Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                                MainStockItems = new ObservableCollection<MainStockDTO>(mainStocks);

                                TotalProducts = products.Count();
                                TotalPages = (int)Math.Ceiling(TotalProducts / (double)PageSize);
                                UpdateVisiblePageNumbers();

                                CalculateAggregatedValues();
                                if (SelectedProduct != null)
                                {
                                    CalculateSelectedProductValues();
                                }

                                Debug.WriteLine($"ProductViewModel: Data refresh complete. {Products.Count} products loaded.");
                            });

                            return true;
                        });

                        success = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ProductViewModel: Error during forced refresh (attempt {attempt + 1}): {ex.Message}");
                        await Task.Delay(500 * (attempt + 1));
                    }
                }

                if (!success)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            "Unable to refresh product data after multiple attempts. Please try again later.",
                            "Refresh Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    });
                }
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
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
                    var mainStockTask = _mainStockService.GetAllAsync();

                    var totalCount = await GetTotalProductCount();
                    if (linkedCts.Token.IsCancellationRequested) return;

                    int calculatedTotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
                    TotalPages = calculatedTotalPages;
                    TotalProducts = totalCount;

                    var products = await GetPagedProductsWithMainStockSync(CurrentPage, PageSize, SearchText);
                    if (linkedCts.Token.IsCancellationRequested) return;

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

        private async Task<List<ProductDTO>> GetPagedProductsWithMainStockSync(int page, int pageSize, string searchText)
        {
            var allProducts = await _productService.GetAllAsync();
            var mainStockItems = await _mainStockService.GetAllAsync();
            var mainStockLookup = mainStockItems.ToDictionary(m => m.MainStockId);

            foreach (var product in allProducts.Where(p => p.MainStockId.HasValue))
            {
                if (mainStockLookup.TryGetValue(product.MainStockId.Value, out var mainStock))
                {
                    product.PurchasePrice = mainStock.PurchasePrice;
                    product.SalePrice = mainStock.SalePrice;
                    product.BoxPurchasePrice = mainStock.BoxPurchasePrice;
                    product.BoxSalePrice = mainStock.BoxSalePrice;
                    product.ItemsPerBox = mainStock.ItemsPerBox;
                }
            }

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

        private async Task SyncWithMainStockAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Another operation is in progress. Please wait.");
                return;
            }

            try
            {
                IsSaving = true;
                StatusMessage = "Synchronizing with MainStock...";

                var productsWithMainStock = Products.Where(p => p.MainStockId.HasValue).ToList();

                if (!productsWithMainStock.Any())
                {
                    StatusMessage = "No products linked to MainStock found.";
                    await Task.Delay(2000);
                    return;
                }

                int syncCount = 0;
                int errorCount = 0;

                var mainStockItems = await _mainStockService.GetAllAsync();
                var mainStockLookup = mainStockItems.ToDictionary(m => m.MainStockId);

                foreach (var product in productsWithMainStock)
                {
                    try
                    {
                        StatusMessage = $"Synchronizing product {product.Name}...";

                        if (mainStockLookup.TryGetValue(product.MainStockId.Value, out var mainStock))
                        {
                            bool changes = false;

                            if (Math.Abs(product.PurchasePrice - mainStock.PurchasePrice) > 0.001m)
                            {
                                product.PurchasePrice = mainStock.PurchasePrice;
                                changes = true;
                            }

                            if (Math.Abs(product.SalePrice - mainStock.SalePrice) > 0.001m)
                            {
                                product.SalePrice = mainStock.SalePrice;
                                changes = true;
                            }

                            if (Math.Abs(product.BoxPurchasePrice - mainStock.BoxPurchasePrice) > 0.001m)
                            {
                                product.BoxPurchasePrice = mainStock.BoxPurchasePrice;
                                changes = true;
                            }

                            if (Math.Abs(product.BoxSalePrice - mainStock.BoxSalePrice) > 0.001m)
                            {
                                product.BoxSalePrice = mainStock.BoxSalePrice;
                                changes = true;
                            }

                            if (product.ItemsPerBox != mainStock.ItemsPerBox)
                            {
                                product.ItemsPerBox = mainStock.ItemsPerBox;
                                changes = true;
                            }

                            if (changes)
                            {
                                var updatedProduct = new ProductDTO
                                {
                                    ProductId = product.ProductId,
                                    Name = product.Name,
                                    Barcode = product.Barcode,
                                    BoxBarcode = product.BoxBarcode,
                                    CategoryId = product.CategoryId,
                                    CategoryName = product.CategoryName,
                                    SupplierId = product.SupplierId,
                                    SupplierName = product.SupplierName,
                                    Description = product.Description,
                                    MainStockId = product.MainStockId,
                                    CurrentStock = product.CurrentStock,
                                    MinimumStock = product.MinimumStock,
                                    ImagePath = product.ImagePath,
                                    Speed = product.Speed,
                                    IsActive = product.IsActive,
                                    CreatedAt = product.CreatedAt,
                                    UpdatedAt = DateTime.Now,
                                    PurchasePrice = mainStock.PurchasePrice,
                                    SalePrice = mainStock.SalePrice,
                                    BoxPurchasePrice = mainStock.BoxPurchasePrice,
                                    BoxSalePrice = mainStock.BoxSalePrice,
                                    ItemsPerBox = mainStock.ItemsPerBox,
                                    MinimumBoxStock = mainStock.MinimumBoxStock
                                };

                                await _productService.UpdateAsync(updatedProduct);

                                product.PurchasePrice = mainStock.PurchasePrice;
                                product.SalePrice = mainStock.SalePrice;
                                product.BoxPurchasePrice = mainStock.BoxPurchasePrice;
                                product.BoxSalePrice = mainStock.BoxSalePrice;
                                product.ItemsPerBox = mainStock.ItemsPerBox;
                                product.UpdatedAt = DateTime.Now;

                                syncCount++;

                                if (syncCount % 5 == 0)
                                {
                                    StatusMessage = $"Synchronized {syncCount} products...";
                                    await Task.Delay(10);
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Warning: Referenced MainStock ID {product.MainStockId} not found for product {product.ProductId}");
                            errorCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error syncing product {product.ProductId}: {ex.Message}");
                        errorCount++;
                    }
                }

                CalculateAggregatedValues();
                if (SelectedProduct != null)
                {
                    CalculateSelectedProductValues();
                }

                StatusMessage = $"Synchronized {syncCount} products with MainStock data.";

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (errorCount > 0)
                    {
                        MessageBox.Show($"Successfully synchronized {syncCount} products with MainStock data.\n\n{errorCount} product(s) had errors during synchronization. See application logs for details.",
                            "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"Successfully synchronized {syncCount} products with MainStock data.",
                            "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error synchronizing with MainStock: {ex.Message}";
                Debug.WriteLine($"Error in SyncWithMainStockAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error during synchronization: {ex.Message}",
                        "Synchronization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsSaving = false;
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
                _eventAggregator.Unsubscribe<GlobalDataRefreshEvent>(HandleGlobalRefresh);
                _isDisposed = true;
            }

            base.Dispose();
        }
    }
}