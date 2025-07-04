// File: QuickTechSystems\ViewModels\Supplier\SupplierInvoiceProductViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Threading;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.Helpers;
using System.Collections.Generic;

namespace QuickTechSystems.ViewModels.Supplier
{
    public class SupplierInvoiceProductViewModel : ViewModelBase
    {
        private readonly ISupplierInvoiceService _supplierInvoiceService;
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private Dictionary<int, SupplierInvoiceDetailDTO> _originalDetails = new Dictionary<int, SupplierInvoiceDetailDTO>();
        private HashSet<int> _modifiedDetailIds = new HashSet<int>();
        private SupplierInvoiceDTO _invoice;
        private ObservableCollection<SupplierInvoiceDetailDTO> _invoiceDetails;
        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private SupplierInvoiceDetailDTO _selectedDetail;
        private SupplierInvoiceDetailDTO _newProductRow;
        private NewProductFromInvoiceDTO _newProductFromInvoice;
        private bool _isLoading;
        private bool _isNewProductDialogOpen;
        private ProductDTO _editingExistingProduct;
        private SupplierInvoiceDetailDTO _editingExistingDetail;
        private string _searchText = string.Empty;
        private bool _hasChanges;
        private string _newProductValidationMessage = string.Empty;
        private bool _hasNewProductValidationMessage;

        // New properties for product search functionality
        private string _productNameSearch = string.Empty;
        private ObservableCollection<ProductDTO> _searchResults;
        private bool _showSearchResults;
        private ProductDTO _selectedSearchResult;
        private string _quickBarcodeSearch = string.Empty;

        private Timer _searchTimer;
        private CancellationTokenSource _searchCancellationTokenSource;

        #region Properties

        public SupplierInvoiceDTO Invoice
        {
            get => _invoice;
            set => SetProperty(ref _invoice, value);
        }

        public ObservableCollection<SupplierInvoiceDetailDTO> InvoiceDetails
        {
            get => _invoiceDetails;
            set => SetProperty(ref _invoiceDetails, value);
        }
        public string QuickBarcodeSearch
        {
            get => _quickBarcodeSearch;
            set => SetProperty(ref _quickBarcodeSearch, value);
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

        public SupplierInvoiceDetailDTO SelectedDetail
        {
            get => _selectedDetail;
            set => SetProperty(ref _selectedDetail, value);
        }

        public SupplierInvoiceDetailDTO NewProductRow
        {
            get => _newProductRow;
            set => SetProperty(ref _newProductRow, value);
        }

        public NewProductFromInvoiceDTO NewProductFromInvoice
        {
            get => _newProductFromInvoice;
            set => SetProperty(ref _newProductFromInvoice, value);
        }
        public Dictionary<int, SupplierInvoiceDetailDTO> OriginalDetails
        {
            get => _originalDetails;
            set => SetProperty(ref _originalDetails, value);
        }
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsNewProductDialogOpen
        {
            get => _isNewProductDialogOpen;
            set => SetProperty(ref _isNewProductDialogOpen, value);
        }

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public bool HasChanges
        {
            get => _hasChanges;
            set => SetProperty(ref _hasChanges, value);
        }

        public string NewProductValidationMessage
        {
            get => _newProductValidationMessage;
            set => SetProperty(ref _newProductValidationMessage, value);
        }

        public bool HasNewProductValidationMessage
        {
            get => _hasNewProductValidationMessage;
            set => SetProperty(ref _hasNewProductValidationMessage, value);
        }

        // New properties for product search
        public string ProductNameSearch
        {
            get => _productNameSearch;
            set
            {
                if (SetProperty(ref _productNameSearch, value))
                {
                    DebouncedProductSearch(value);
                }
            }
        }

        public ObservableCollection<ProductDTO> SearchResults
        {
            get => _searchResults;
            set => SetProperty(ref _searchResults, value);
        }

        public bool ShowSearchResults
        {
            get => _showSearchResults;
            set => SetProperty(ref _showSearchResults, value);
        }

        public ProductDTO SelectedSearchResult
        {
            get => _selectedSearchResult;
            set => SetProperty(ref _selectedSearchResult, value);
        }

        #endregion

        #region Commands

        public ICommand LoadDataCommand { get; }
        public ICommand SaveChangesCommand { get; }
        public ICommand CancelChangesCommand { get; }
        public ICommand AddRowCommand { get; }
        public ICommand AddProductCommand { get; }
        public ICommand RemoveProductCommand { get; }
        public ICommand ProductSelectedCommand { get; }
        public ICommand BarcodeChangedCommand { get; }
        public ICommand SaveAndCloseCommand { get; }
        public ICommand OpenNewProductDialogCommand { get; }
        public ICommand SaveNewProductCommand { get; }
        public ICommand CancelNewProductCommand { get; }
        public ICommand GenerateBarcodeCommand { get; }
        public ICommand SelectSearchResultCommand { get; } // New command
        public ICommand ClearSearchCommand { get; } // New command
        public ICommand GenerateItemBarcodeCommand { get; }
        public ICommand GenerateBoxBarcodeCommand { get; }

        #endregion

        public SupplierInvoiceProductViewModel(
            ISupplierInvoiceService supplierInvoiceService,
            IProductService productService,
            ICategoryService categoryService,
            ISupplierService supplierService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _supplierInvoiceService = supplierInvoiceService ?? throw new ArgumentNullException(nameof(supplierInvoiceService));
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));
            GenerateItemBarcodeCommand = new RelayCommand(_ => GenerateItemBarcode());
            GenerateBoxBarcodeCommand = new RelayCommand(_ => GenerateBoxBarcode());
            // Initialize collections
            InvoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>();
            Products = new ObservableCollection<ProductDTO>();
            Categories = new ObservableCollection<CategoryDTO>();
            Suppliers = new ObservableCollection<SupplierDTO>();
            SearchResults = new ObservableCollection<ProductDTO>();
            SaveAndCloseCommand = new AsyncRelayCommand(async _ => await SaveAndCloseAsync());
            // Initialize commands
            LoadDataCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            SaveChangesCommand = new AsyncRelayCommand(async _ => await SaveChangesAsync());
            CancelChangesCommand = new RelayCommand(_ => CancelChanges());
            AddRowCommand = new RelayCommand(_ => AddNewRow());


            AddProductCommand = new AsyncRelayCommand(async _ => await AddProductAsync());
            RemoveProductCommand = new RelayCommand<object>(param =>
            {
                if (param is SupplierInvoiceDetailDTO detail)
                    RemoveDetail(detail);
            });
            ProductSelectedCommand = new RelayCommand<object>(param =>
            {
                if (param is ProductDTO product)
                    OnProductSelected(param);
            });
            BarcodeChangedCommand = new RelayCommand<object>(param =>
            {
                if (param is string barcode && !string.IsNullOrWhiteSpace(barcode))
                    _ = Task.Run(async () => await OnBarcodeChanged(param));
            });
            OpenNewProductDialogCommand = new AsyncRelayCommand(async _ => await OpenNewProductDialogAsync());
            SaveNewProductCommand = new AsyncRelayCommand(async _ => await SaveNewProductAsync());
            CancelNewProductCommand = new RelayCommand(_ => CancelNewProduct());
            GenerateBarcodeCommand = new RelayCommand<object>(param =>
            {
                if (param is SupplierInvoiceDetailDTO detail)
                    _ = Task.Run(async () => await GenerateBarcodeAsync(param));
            });
            SelectSearchResultCommand = new RelayCommand<object>(OnSearchResultSelected); // New command
            ClearSearchCommand = new RelayCommand(_ => ClearProductSearch()); // New command

            // Initialize search timer
            _searchTimer = new Timer(OnSearchTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }

        public async Task InitializeAsync(SupplierInvoiceDTO invoice)
        {
            if (invoice == null) return;

            Invoice = invoice;
            await LoadDataAsync();
        }
        private async Task SaveAndCloseAsync()
        {
            try
            {
                // First save the changes
                await SaveChangesAsync();

                // If save was successful and no errors occurred, request window close
                if (!HasChanges) // HasChanges becomes false after successful save
                {
                    RequestWindowClose?.Invoke();
                }
            }
            catch (Exception ex)
            {
                // Error handling is already done in SaveChangesAsync
                System.Diagnostics.Debug.WriteLine($"[SaveAndClose] Error: {ex.Message}");
            }
        }

        public event Action RequestWindowClose;
        #region Data Loading
        private void GenerateItemBarcode()
        {
            try
            {
                if (NewProductFromInvoice == null)
                {
                    System.Windows.MessageBox.Show("No product data available.", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                var newBarcode = GenerateUniqueBarcode();
                NewProductFromInvoice.Barcode = newBarcode;

                // No popup needed - barcode is automatically set in the field
                System.Diagnostics.Debug.WriteLine($"Generated item barcode: {newBarcode}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error generating barcode: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void CleanupPropertyChangeEvents()
        {
            if (InvoiceDetails != null)
            {
                foreach (var detail in InvoiceDetails)
                {
                    if (detail != null)
                    {
                        detail.PropertyChanged -= OnDetailPropertyChanged;
                    }
                }
            }
        }


        private void GenerateBoxBarcode()
        {
            try
            {
                if (NewProductFromInvoice == null)
                {
                    System.Windows.MessageBox.Show("No product data available.", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                var newBarcode = GenerateUniqueBarcode();
                NewProductFromInvoice.BoxBarcode = newBarcode;

                // No popup needed - barcode is automatically set in the field
                System.Diagnostics.Debug.WriteLine($"Generated box barcode: {newBarcode}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error generating box barcode: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private string GenerateUniqueBarcode()
        {
            // Generate a unique barcode with 5-9 digits
            var random = new Random();

            // Randomly choose length between 5-9 digits
            var length = random.Next(5, 10); // 5 to 9 inclusive

            // Generate random number with the chosen length
            var minValue = (int)Math.Pow(10, length - 1); // e.g., 10000 for 5 digits
            var maxValue = (int)Math.Pow(10, length) - 1;  // e.g., 99999 for 5 digits

            var barcode = random.Next(minValue, maxValue + 1).ToString();

            return barcode;
        }
        protected override async Task LoadDataAsync()
        {
            if (!await _operationLock.WaitAsync(5000)) // 5 second timeout
            {
                System.Diagnostics.Debug.WriteLine("LoadDataAsync: Could not acquire lock within 5 seconds");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("LoadDataAsync: Starting data load");
                IsLoading = true;

                var tasks = new List<Task>
        {
            LoadInvoiceDetailsAsync(),
            LoadProductsAsync(),
            LoadCategoriesAsync(),
            LoadSuppliersAsync()
        };

                await Task.WhenAll(tasks);

                InitializeNewProductRow();
                HasChanges = false;

                System.Diagnostics.Debug.WriteLine("LoadDataAsync: Data load completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDataAsync: ERROR: {ex}");
                System.Windows.MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
                System.Diagnostics.Debug.WriteLine("LoadDataAsync: Completed, lock released");
            }
        }

        private async Task LoadInvoiceDetailsAsync()
        {
            try
            {
                if (Invoice?.SupplierInvoiceId > 0)
                {
                    var invoice = await _supplierInvoiceService.GetByIdAsync(Invoice.SupplierInvoiceId);
                    if (invoice?.Details != null)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            InvoiceDetails.Clear();
                            OriginalDetails.Clear();
                            _modifiedDetailIds.Clear();

                            foreach (var detail in invoice.Details)
                            {
                                InvoiceDetails.Add(detail);

                                // Store a copy of the original values for change tracking
                                if (detail.SupplierInvoiceDetailId > 0)
                                {
                                    OriginalDetails[detail.SupplierInvoiceDetailId] = CreateDetailCopy(detail);
                                }
                            }

                            // Setup property change notifications for all items
                            SetupInvoiceDetailsPropertyChanges();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading invoice details: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task LoadProductsAsync()
        {
            try
            {
                var products = await _productService.GetActiveAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Products.Clear();
                    foreach (var product in products)
                    {
                        Products.Add(product);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading products: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var categories = await _categoryService.GetProductCategoriesAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Categories.Clear();
                    foreach (var category in categories)
                    {
                        Categories.Add(category);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading categories: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task LoadSuppliersAsync()
        {
            try
            {
                var suppliers = await _supplierService.GetActiveAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Suppliers.Clear();
                    foreach (var supplier in suppliers)
                    {
                        Suppliers.Add(supplier);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading suppliers: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        #endregion

        #region Product Management

        private void InitializeNewProductRow()
        {
            try
            {
                NewProductRow = new SupplierInvoiceDetailDTO
                {
                    SupplierInvoiceId = Invoice?.SupplierInvoiceId ?? 0,
                    Quantity = 1,
                    PurchasePrice = 0,
                    TotalPrice = 0,
                    ItemsPerBox = 1
                };
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error initializing new product row: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void AddNewRow()
        {
            try
            {
                if (NewProductRow == null)
                {
                    InitializeNewProductRow();
                    return;
                }

                // Validate new product row
                if (NewProductRow.ProductId <= 0)
                {
                    System.Windows.MessageBox.Show("Please select a product before adding.", "Invalid Product",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (NewProductRow.Quantity <= 0)
                {
                    System.Windows.MessageBox.Show("Please enter a valid quantity.", "Invalid Quantity",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (NewProductRow.PurchasePrice <= 0)
                {
                    System.Windows.MessageBox.Show("Please enter a valid purchase price.", "Invalid Price",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // Check if product already exists in the invoice
                var existingDetail = InvoiceDetails.FirstOrDefault(d => d.ProductId == NewProductRow.ProductId);
                if (existingDetail != null)
                {
                    var result = System.Windows.MessageBox.Show(
                        $"Product '{NewProductRow.ProductName}' is already in this invoice. Do you want to combine the quantities?",
                        "Product Already Exists",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        // Combine quantities and recalculate average price
                        var totalQuantity = existingDetail.Quantity + NewProductRow.Quantity;
                        var totalValue = (existingDetail.Quantity * existingDetail.PurchasePrice) +
                                       (NewProductRow.Quantity * NewProductRow.PurchasePrice);
                        var averagePrice = totalValue / totalQuantity;

                        existingDetail.Quantity = totalQuantity;
                        existingDetail.PurchasePrice = Math.Round(averagePrice, 3);
                        existingDetail.TotalPrice = existingDetail.Quantity * existingDetail.PurchasePrice;
                        HasChanges = true;
                    }
                }
                else
                {
                    // Add new detail
                    var newDetail = CreateDetailCopy(NewProductRow);
                    newDetail.TotalPrice = newDetail.Quantity * newDetail.PurchasePrice;

                    InvoiceDetails.Add(newDetail);

                    // CRITICAL FIX: Setup property change notification for the new item
                    SetupPropertyChangeForDetail(newDetail);

                    HasChanges = true;
                }

                // Clear the form
                InitializeNewProductRow();
                ClearProductSearch();

                System.Windows.MessageBox.Show("Product added successfully!", "Success",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error adding new row: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        private void OnProductSelected(object param)
        {
            if (param is ProductDTO product)
            {
                try
                {
                    // Check if product already exists in the invoice
                    var existingDetail = InvoiceDetails.FirstOrDefault(d => d.ProductId == product.ProductId);
                    if (existingDetail != null)
                    {
                        var result = System.Windows.MessageBox.Show(
                            $"Product '{product.Name}' is already in this invoice with quantity {existingDetail.Quantity}.\n\n" +
                            $"Would you like to:\n" +
                            $"• Click 'Yes' to open the dialog to modify this product\n" +
                            $"• Click 'No' to cancel",
                            "Product Already Exists",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);

                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            // Open dialog to modify existing product in invoice
                            OpenProductDialogForExistingProduct(product, existingDetail);
                        }

                        // Clear the search fields
                        ClearProductSearch();
                        QuickBarcodeSearch = string.Empty;
                        return;
                    }

                    // Product not in invoice yet - open dialog with product data pre-populated
                    OpenProductDialogForExistingProduct(product, null);

                    // Clear the search fields
                    ClearProductSearch();
                    QuickBarcodeSearch = string.Empty;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error handling product selection: {ex.Message}",
                        "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void OpenProductDialogForExistingProduct(ProductDTO existingProduct, SupplierInvoiceDetailDTO existingDetail = null)
        {
            try
            {
                // Create NewProductFromInvoice populated with existing product data
                NewProductFromInvoice = new NewProductFromInvoiceDTO
                {
                    // If we're editing an existing detail, use its values, otherwise use product defaults
                    Name = existingProduct.Name,
                    Barcode = existingProduct.Barcode,
                    Description = existingProduct.Description,
                    CategoryId = existingProduct.CategoryId,
                    CategoryName = existingProduct.CategoryName,
                    SupplierId = existingProduct.SupplierId ?? (Invoice?.SupplierId ?? 0),
                    SupplierName = existingProduct.SupplierName ?? (Invoice?.SupplierName ?? string.Empty),

                    // Use values from existing detail if available, otherwise use product values
                    PurchasePrice = existingDetail?.PurchasePrice ?? existingProduct.PurchasePrice,
                    SalePrice = existingDetail?.SalePrice ?? existingProduct.SalePrice,
                    MinimumStock = existingDetail?.MinimumStock ?? existingProduct.MinimumStock,
                    WholesalePrice = existingDetail?.WholesalePrice ?? existingProduct.WholesalePrice,

                    // FOR RESTOCKING: Set stock values to 1 (amount to ADD, not replace)
                    CurrentStock = 1, // Amount to add to current stock
                    Storehouse = 1,   // Amount to add to storehouse

                    // Box information
                    BoxBarcode = existingDetail?.BoxBarcode ?? existingProduct.BoxBarcode,
                    NumberOfBoxes = existingDetail?.NumberOfBoxes ?? existingProduct.NumberOfBoxes,
                    ItemsPerBox = existingDetail?.ItemsPerBox ?? existingProduct.ItemsPerBox,
                    BoxPurchasePrice = existingDetail?.BoxPurchasePrice ?? existingProduct.BoxPurchasePrice,
                    BoxSalePrice = existingDetail?.BoxSalePrice ?? existingProduct.BoxSalePrice,
                    BoxWholesalePrice = existingDetail?.BoxWholesalePrice ?? existingProduct.BoxWholesalePrice,

                    IsActive = existingProduct.IsActive,
                    ImagePath = existingProduct.ImagePath,

                    // Set the invoice quantity (default to existing detail quantity or 1)
                    InvoiceQuantity = existingDetail?.Quantity ?? 1
                };

                // Store reference to existing product and detail for update logic
                _editingExistingProduct = existingProduct;
                _editingExistingDetail = existingDetail;

                IsNewProductDialogOpen = true;

                System.Windows.MessageBox.Show(
                    existingDetail != null
                        ? $"Editing existing product '{existingProduct.Name}' in this invoice.\n\nCurrent Stock: {existingProduct.CurrentStock}\nStorehouse: {existingProduct.Storehouse}\n\nThe stock values shown are amounts to ADD to existing stock."
                        : $"Adding existing product '{existingProduct.Name}' to invoice for restocking.\n\nCurrent Stock: {existingProduct.CurrentStock}\nStorehouse: {existingProduct.Storehouse}\n\nThe stock values shown are amounts to ADD to existing stock.",
                    "Restocking Product",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening product dialog: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        private void SetupPropertyChangeForDetail(SupplierInvoiceDetailDTO detail)
        {
            if (detail != null)
            {
                // Remove existing handler first to avoid duplicates
                detail.PropertyChanged -= OnDetailPropertyChanged;
                // Add new handler
                detail.PropertyChanged += OnDetailPropertyChanged;
            }
        }
        private void UpdateNewProductCalculations()
        {
            try
            {
                if (NewProductRow != null)
                {
                    NewProductRow.TotalPrice = NewProductRow.Quantity * NewProductRow.PurchasePrice;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error updating calculations: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task OnBarcodeChanged(object param)
        {
            if (param is string barcode && !string.IsNullOrWhiteSpace(barcode))
            {
                try
                {
                    var product = await _productService.GetByBarcodeAsync(barcode);

                    // Ensure UI operations run on UI thread
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        if (product != null)
                        {
                            var result = System.Windows.MessageBox.Show(
                                $"Found existing product: '{product.Name}' (Current Stock: {product.CurrentStock + product.Storehouse})\n\n" +
                                $"Would you like to:\n" +
                                $"• Click 'Yes' to add this existing product to restock\n" +
                                $"• Click 'No' to clear the barcode field",
                                "Existing Product Found",
                                System.Windows.MessageBoxButton.YesNo,
                                System.Windows.MessageBoxImage.Question);

                            if (result == System.Windows.MessageBoxResult.Yes)
                            {
                                OnProductSelected(product);
                                System.Windows.MessageBox.Show(
                                    $"Product '{product.Name}' has been loaded. Please enter the quantity and purchase price for this restock.",
                                    "Product Loaded",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Information);
                            }
                            else
                            {
                                // Clear the barcode field
                                if (NewProductRow != null)
                                {
                                    NewProductRow.ProductBarcode = string.Empty;
                                }
                                QuickBarcodeSearch = string.Empty;
                            }
                        }
                        else
                        {
                            var result = System.Windows.MessageBox.Show(
                                $"Product with barcode '{barcode}' not found. Would you like to create a new product?",
                                "Product Not Found",
                                System.Windows.MessageBoxButton.YesNo,
                                System.Windows.MessageBoxImage.Question);

                            if (result == System.Windows.MessageBoxResult.Yes)
                            {
                                await OpenNewProductDialogAsync();
                                if (NewProductFromInvoice != null)
                                {
                                    NewProductFromInvoice.Barcode = barcode;
                                }
                            }
                            else
                            {
                                // Clear the barcode field
                                QuickBarcodeSearch = string.Empty;
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.MessageBox.Show($"Error finding product by barcode: {ex.Message}", "Error",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    });
                }
            }
        }

        #endregion

        #region NEW: Product Search Functionality

        private void DebouncedProductSearch(string searchTerm)
        {
            // Cancel previous search
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource = new CancellationTokenSource();

            // Reset timer - 300ms delay
            _searchTimer?.Change(300, Timeout.Infinite);
        }

        private async void OnSearchTimerElapsed(object state)
        {
            var searchTerm = ProductNameSearch;
            var cancellationToken = _searchCancellationTokenSource?.Token ?? CancellationToken.None;

            if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SearchResults.Clear();
                    ShowSearchResults = false;
                });
                return;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                var products = await _productService.SearchByNameAsync(searchTerm);

                if (cancellationToken.IsCancellationRequested) return;

                var filteredProducts = products?.Where(p =>
                    p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) && p.IsActive)
                    .Take(10)
                    .ToList() ?? new List<ProductDTO>();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SearchResults.Clear();
                    foreach (var product in filteredProducts)
                    {
                        SearchResults.Add(product);
                    }
                    ShowSearchResults = SearchResults.Any();
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when operation is cancelled
            }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show($"Error searching products: {ex.Message}", "Search Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                });
            }
        }

        private void OnSearchResultSelected(object param)
        {
            if (param is ProductDTO selectedProduct)
            {
                OnProductSelected(selectedProduct);
                ClearProductSearch();
            }
        }

        private void ClearProductSearch()
        {
            ProductNameSearch = string.Empty;
            SearchResults.Clear();
            ShowSearchResults = false;
            SelectedSearchResult = null;
        }

        #endregion

        #region Product Dialog Management

        private async Task AddProductAsync()
        {
            AddNewRow();
            await Task.CompletedTask;
        }

        private async Task RemoveProductAsync(object param)
        {
            if (param is SupplierInvoiceDetailDTO detail)
            {
                RemoveDetail(detail);
            }
            await Task.CompletedTask;
        }

        private void RemoveDetail(SupplierInvoiceDetailDTO detail)
        {
            if (detail != null)
            {
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to remove {detail.ProductName} from the invoice?",
                    "Confirm Removal",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    InvoiceDetails.Remove(detail);
                    HasChanges = true;
                }
            }
        }

        private async Task OpenNewProductDialogAsync()
        {
            try
            {
                NewProductFromInvoice = new NewProductFromInvoiceDTO
                {
                    SupplierId = Invoice?.SupplierId ?? 0,
                    SupplierName = Invoice?.SupplierName ?? string.Empty,
                    ItemsPerBox = 1
                };

                IsNewProductDialogOpen = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening new product dialog: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            await Task.CompletedTask;
        }

        private async Task SaveNewProductAsync()
        {
            try
            {
                if (NewProductFromInvoice == null)
                {
                    System.Windows.MessageBox.Show("No product data available.", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                // Basic validation
                if (string.IsNullOrWhiteSpace(NewProductFromInvoice.Name))
                {
                    SetNewProductValidationMessage("Product name is required.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(NewProductFromInvoice.Barcode))
                {
                    SetNewProductValidationMessage("Barcode is required.");
                    return;
                }

                if (NewProductFromInvoice.CategoryId <= 0)
                {
                    SetNewProductValidationMessage("Please select a category.");
                    return;
                }

                if (NewProductFromInvoice.PurchasePrice <= 0)
                {
                    SetNewProductValidationMessage("Purchase price must be greater than 0.");
                    return;
                }

                if (NewProductFromInvoice.SalePrice <= 0)
                {
                    SetNewProductValidationMessage("Sale price must be greater than 0.");
                    return;
                }

                if (NewProductFromInvoice.InvoiceQuantity <= 0)
                {
                    SetNewProductValidationMessage("Invoice quantity must be greater than 0.");
                    return;
                }

                ClearNewProductValidationMessage();

                // Check if we're editing an existing product
                if (_editingExistingProduct != null)
                {
                    await HandleExistingProductSave();
                }
                else
                {
                    // Create new product
                    var createdProduct = await _supplierInvoiceService.CreateNewProductAndAddToInvoiceAsync(
                        NewProductFromInvoice, Invoice.SupplierInvoiceId);

                    if (createdProduct != null)
                    {
                        System.Windows.MessageBox.Show("New product created and added to invoice successfully!", "Success",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }

                IsNewProductDialogOpen = false;
                await LoadDataAsync();

                // Clear references
                _editingExistingProduct = null;
                _editingExistingDetail = null;
            }
            catch (Exception ex)
            {
                SetNewProductValidationMessage($"Error saving product: {ex.Message}");
            }
        }

        private async Task HandleExistingProductSave()
        {
            try
            {
                // Store original values for calculation
                var originalCurrentStock = _editingExistingProduct.CurrentStock;
                var originalStorehouse = _editingExistingProduct.Storehouse;

                // Update the existing product with new data (ADD stock values, update prices)
                _editingExistingProduct.PurchasePrice = NewProductFromInvoice.PurchasePrice;
                _editingExistingProduct.SalePrice = NewProductFromInvoice.SalePrice;

                // ADD to existing stock (restocking operation)
                _editingExistingProduct.CurrentStock = originalCurrentStock + NewProductFromInvoice.CurrentStock;
                _editingExistingProduct.Storehouse = originalStorehouse + NewProductFromInvoice.Storehouse;

                _editingExistingProduct.MinimumStock = NewProductFromInvoice.MinimumStock;
                _editingExistingProduct.WholesalePrice = NewProductFromInvoice.WholesalePrice;
                _editingExistingProduct.BoxBarcode = NewProductFromInvoice.BoxBarcode;
                _editingExistingProduct.NumberOfBoxes = NewProductFromInvoice.NumberOfBoxes;
                _editingExistingProduct.ItemsPerBox = NewProductFromInvoice.ItemsPerBox;
                _editingExistingProduct.BoxPurchasePrice = NewProductFromInvoice.BoxPurchasePrice;
                _editingExistingProduct.BoxSalePrice = NewProductFromInvoice.BoxSalePrice;
                _editingExistingProduct.BoxWholesalePrice = NewProductFromInvoice.BoxWholesalePrice;

                // Update product in database
                await _productService.UpdateAsync(_editingExistingProduct);

                // Handle invoice detail
                if (_editingExistingDetail != null)
                {
                    // Update existing detail
                    _editingExistingDetail.Quantity = NewProductFromInvoice.InvoiceQuantity;
                    _editingExistingDetail.PurchasePrice = NewProductFromInvoice.PurchasePrice;
                    _editingExistingDetail.SalePrice = NewProductFromInvoice.SalePrice;
                    _editingExistingDetail.CurrentStock = NewProductFromInvoice.CurrentStock; // Amount being added
                    _editingExistingDetail.Storehouse = NewProductFromInvoice.Storehouse;     // Amount being added
                    _editingExistingDetail.WholesalePrice = NewProductFromInvoice.WholesalePrice;
                    _editingExistingDetail.BoxBarcode = NewProductFromInvoice.BoxBarcode;
                    _editingExistingDetail.NumberOfBoxes = NewProductFromInvoice.NumberOfBoxes;
                    _editingExistingDetail.ItemsPerBox = NewProductFromInvoice.ItemsPerBox;
                    _editingExistingDetail.BoxPurchasePrice = NewProductFromInvoice.BoxPurchasePrice;
                    _editingExistingDetail.BoxSalePrice = NewProductFromInvoice.BoxSalePrice;
                    _editingExistingDetail.BoxWholesalePrice = NewProductFromInvoice.BoxWholesalePrice;
                    _editingExistingDetail.MinimumStock = NewProductFromInvoice.MinimumStock;
                    _editingExistingDetail.TotalPrice = _editingExistingDetail.Quantity * _editingExistingDetail.PurchasePrice;

                    await _supplierInvoiceService.UpdateInvoiceDetailAsync(_editingExistingDetail);

                    System.Windows.MessageBox.Show(
                        $"Product restocked successfully!\n\n" +
                        $"Stock Changes:\n" +
                        $"• Current Stock: {originalCurrentStock} + {NewProductFromInvoice.CurrentStock} = {_editingExistingProduct.CurrentStock}\n" +
                        $"• Storehouse: {originalStorehouse} + {NewProductFromInvoice.Storehouse} = {_editingExistingProduct.Storehouse}",
                        "Restocking Complete",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    // Create new invoice detail for existing product
                    var newDetail = new SupplierInvoiceDetailDTO
                    {
                        SupplierInvoiceId = Invoice.SupplierInvoiceId,
                        ProductId = _editingExistingProduct.ProductId,
                        ProductName = _editingExistingProduct.Name,
                        ProductBarcode = _editingExistingProduct.Barcode,
                        Quantity = NewProductFromInvoice.InvoiceQuantity,
                        PurchasePrice = NewProductFromInvoice.PurchasePrice,
                        SalePrice = NewProductFromInvoice.SalePrice,
                        CurrentStock = NewProductFromInvoice.CurrentStock, // Amount being added
                        Storehouse = NewProductFromInvoice.Storehouse,     // Amount being added
                        WholesalePrice = NewProductFromInvoice.WholesalePrice,
                        BoxBarcode = NewProductFromInvoice.BoxBarcode,
                        NumberOfBoxes = NewProductFromInvoice.NumberOfBoxes,
                        ItemsPerBox = NewProductFromInvoice.ItemsPerBox,
                        BoxPurchasePrice = NewProductFromInvoice.BoxPurchasePrice,
                        BoxSalePrice = NewProductFromInvoice.BoxSalePrice,
                        BoxWholesalePrice = NewProductFromInvoice.BoxWholesalePrice,
                        MinimumStock = NewProductFromInvoice.MinimumStock,
                        CategoryName = _editingExistingProduct.CategoryName,
                        SupplierName = _editingExistingProduct.SupplierName,
                        TotalPrice = NewProductFromInvoice.InvoiceQuantity * NewProductFromInvoice.PurchasePrice
                    };

                    await _supplierInvoiceService.AddExistingProductToInvoiceAsync(newDetail);

                    System.Windows.MessageBox.Show(
                        $"Product restocked and added to invoice successfully!\n\n" +
                        $"Stock Changes:\n" +
                        $"• Current Stock: {originalCurrentStock} + {NewProductFromInvoice.CurrentStock} = {_editingExistingProduct.CurrentStock}\n" +
                        $"• Storehouse: {originalStorehouse} + {NewProductFromInvoice.Storehouse} = {_editingExistingProduct.Storehouse}",
                        "Restocking Complete",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error handling existing product save: {ex.Message}", ex);
            }
        }
        private void CancelNewProduct()
        {
            IsNewProductDialogOpen = false;
            NewProductFromInvoice = null;
            _editingExistingProduct = null;
            _editingExistingDetail = null;
            ClearNewProductValidationMessage();
        }

        private void SetNewProductValidationMessage(string message)
        {
            NewProductValidationMessage = message;
            HasNewProductValidationMessage = !string.IsNullOrEmpty(message);
        }

        private void ClearNewProductValidationMessage()
        {
            NewProductValidationMessage = string.Empty;
            HasNewProductValidationMessage = false;
        }

        private async Task GenerateBarcodeAsync(object param)
        {
            if (param is SupplierInvoiceDetailDTO detail)
            {
                try
                {
                    var product = Products.FirstOrDefault(p => p.ProductId == detail.ProductId);
                    if (product != null)
                    {
                        var updatedProduct = await _productService.GenerateBarcodeAsync(product);
                        if (updatedProduct != null && !string.IsNullOrEmpty(updatedProduct.Barcode))
                        {
                            detail.ProductBarcode = updatedProduct.Barcode;
                            HasChanges = true;
                            System.Windows.MessageBox.Show("Barcode generated successfully.", "Success",
                                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error generating barcode: {ex.Message}", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Save Changes

        private async Task SaveChangesAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                System.Windows.MessageBox.Show("Save operation already in progress. Please wait.", "Info",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                if (Invoice == null)
                {
                    System.Windows.MessageBox.Show("No invoice available to save.", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                if (InvoiceDetails == null || !InvoiceDetails.Any())
                {
                    System.Windows.MessageBox.Show("No invoice details available to save.", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                IsLoading = true;

                // Get details that need to be added (new ones with ID = 0)
                var detailsToAdd = InvoiceDetails.Where(d => d.SupplierInvoiceDetailId == 0).ToList();

                // Get details that need to be updated (existing ones that have been modified)
                var detailsToUpdate = InvoiceDetails.Where(d =>
                    d.SupplierInvoiceDetailId > 0 &&
                    (_modifiedDetailIds.Contains(d.SupplierInvoiceDetailId) || HasDetailChanged(d))
                ).ToList();

                System.Diagnostics.Debug.WriteLine($"[SaveChanges] Items to add: {detailsToAdd.Count}, Items to update: {detailsToUpdate.Count}");

                int processedCount = 0;

                // Handle new details
                foreach (var detail in detailsToAdd)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveChanges] Adding product: {detail.ProductName}");

                        // Ensure total price is calculated correctly
                        detail.TotalPrice = detail.Quantity * detail.PurchasePrice;

                        if (detail.ProductId > 0)
                        {
                            var existingProduct = Products.FirstOrDefault(p => p.ProductId == detail.ProductId);
                            if (existingProduct != null)
                            {
                                // For existing products, update the product data first
                                await UpdateExistingProductFromInvoiceDetail(detail);
                                await _supplierInvoiceService.AddExistingProductToInvoiceAsync(detail);
                            }
                            else
                            {
                                await _supplierInvoiceService.AddProductToInvoiceAsync(detail);
                            }
                        }
                        else
                        {
                            await _supplierInvoiceService.AddProductToInvoiceAsync(detail);
                        }

                        processedCount++;
                        System.Diagnostics.Debug.WriteLine($"[SaveChanges] Successfully added product: {detail.ProductName}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveChanges] ERROR adding product {detail.ProductName}: {ex}");
                        System.Windows.MessageBox.Show($"Error adding product {detail.ProductName}: {ex.Message}", "Error",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        break;
                    }
                }

                // Handle updated details
                foreach (var detail in detailsToUpdate)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveChanges] Updating product: {detail.ProductName}");

                        // Ensure total price is calculated correctly
                        detail.TotalPrice = detail.Quantity * detail.PurchasePrice;

                        // For existing products, check if product data needs to be updated
                        if (detail.ProductId > 0)
                        {
                            var existingProduct = Products.FirstOrDefault(p => p.ProductId == detail.ProductId);
                            if (existingProduct != null && HasProductDataChanged(detail, existingProduct))
                            {
                                await UpdateExistingProductFromInvoiceDetail(detail);
                            }
                        }

                        await _supplierInvoiceService.UpdateInvoiceDetailAsync(detail);
                        processedCount++;

                        System.Diagnostics.Debug.WriteLine($"[SaveChanges] Successfully updated product: {detail.ProductName}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveChanges] ERROR updating product {detail.ProductName}: {ex}");
                        System.Windows.MessageBox.Show($"Error updating product {detail.ProductName}: {ex.Message}", "Error",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        break;
                    }
                }

                // Reload data to refresh everything and reset change tracking
                if (processedCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine("[SaveChanges] Reloading data...");
                    await LoadDataAsync();

                    // Clear modification tracking
                    _modifiedDetailIds.Clear();
                }

                // Show appropriate success message
                if (processedCount > 0)
                {
                    var addedCount = detailsToAdd.Count;
                    var updatedCount = detailsToUpdate.Count;

                    string message = "";
                    if (addedCount > 0 && updatedCount > 0)
                    {
                        message = $"Successfully added {addedCount} new item(s) and updated {updatedCount} existing item(s).";
                    }
                    else if (addedCount > 0)
                    {
                        message = $"Successfully added {addedCount} new item(s).";
                    }
                    else if (updatedCount > 0)
                    {
                        message = $"Successfully updated {updatedCount} item(s).";
                    }

                    System.Windows.MessageBox.Show(message, "Success",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("No changes to save.", "Info",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }

                HasChanges = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveChanges] FATAL ERROR: {ex}");
                System.Windows.MessageBox.Show($"Fatal error saving changes: {ex.Message}", "Fatal Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
                System.Diagnostics.Debug.WriteLine("[SaveChanges] Operation completed, lock released");
            }
        }

        private bool HasProductDataChanged(SupplierInvoiceDetailDTO detail, ProductDTO originalProduct)
        {
            const decimal tolerance = 0.001m;

            return Math.Abs(detail.PurchasePrice - originalProduct.PurchasePrice) > tolerance ||
                   Math.Abs(detail.SalePrice - originalProduct.SalePrice) > tolerance ||
                   Math.Abs(detail.CurrentStock - originalProduct.CurrentStock) > tolerance ||
                   Math.Abs(detail.Storehouse - originalProduct.Storehouse) > tolerance ||
                   Math.Abs(detail.WholesalePrice - originalProduct.WholesalePrice) > tolerance ||
                   detail.MinimumStock != originalProduct.MinimumStock ||
                   detail.BoxBarcode != originalProduct.BoxBarcode ||
                   detail.NumberOfBoxes != originalProduct.NumberOfBoxes ||
                   detail.ItemsPerBox != originalProduct.ItemsPerBox ||
                   Math.Abs(detail.BoxPurchasePrice - originalProduct.BoxPurchasePrice) > tolerance ||
                   Math.Abs(detail.BoxSalePrice - originalProduct.BoxSalePrice) > tolerance ||
                   Math.Abs(detail.BoxWholesalePrice - originalProduct.BoxWholesalePrice) > tolerance;
        }
        private async Task UpdateExistingProductFromInvoiceDetail(SupplierInvoiceDetailDTO detail)
        {
            try
            {
                var existingProduct = Products.FirstOrDefault(p => p.ProductId == detail.ProductId);
                if (existingProduct == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateExistingProduct] Product {detail.ProductId} not found in cache");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[UpdateExistingProduct] Updating product: {existingProduct.Name}");

                // Update product data from invoice detail
                existingProduct.PurchasePrice = detail.PurchasePrice;
                existingProduct.SalePrice = detail.SalePrice;
                existingProduct.CurrentStock = detail.CurrentStock;
                existingProduct.Storehouse = detail.Storehouse;
                existingProduct.WholesalePrice = detail.WholesalePrice;
                existingProduct.MinimumStock = detail.MinimumStock;

                // Update box-related fields
                existingProduct.BoxBarcode = detail.BoxBarcode ?? string.Empty;
                existingProduct.NumberOfBoxes = detail.NumberOfBoxes;
                existingProduct.ItemsPerBox = detail.ItemsPerBox;
                existingProduct.BoxPurchasePrice = detail.BoxPurchasePrice;
                existingProduct.BoxSalePrice = detail.BoxSalePrice;
                existingProduct.BoxWholesalePrice = detail.BoxWholesalePrice;

                // Update the product in the database
                await _productService.UpdateAsync(existingProduct);

                System.Diagnostics.Debug.WriteLine($"[UpdateExistingProduct] Successfully updated product: {existingProduct.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateExistingProduct] ERROR: {ex}");
                throw new Exception($"Failed to update product data: {ex.Message}", ex);
            }
        }
        private void SetupInvoiceDetailsPropertyChanges()
        {
            if (InvoiceDetails != null)
            {
                foreach (var detail in InvoiceDetails)
                {
                    SetupPropertyChangeForDetail(detail);
                }
            }
        }
        private void DebugShowChanges()
        {
            var detailsToAdd = InvoiceDetails.Where(d => d.SupplierInvoiceDetailId == 0).ToList();
            var detailsToUpdate = InvoiceDetails.Where(d =>
                d.SupplierInvoiceDetailId > 0 &&
                (_modifiedDetailIds.Contains(d.SupplierInvoiceDetailId) || HasDetailChanged(d))
            ).ToList();

            string debugMessage = $"Debug Info:\n";
            debugMessage += $"Total Details: {InvoiceDetails.Count}\n";
            debugMessage += $"Details to Add: {detailsToAdd.Count}\n";
            debugMessage += $"Details to Update: {detailsToUpdate.Count}\n";
            debugMessage += $"Original Details Count: {OriginalDetails.Count}\n";
            debugMessage += $"Modified Detail IDs: {string.Join(", ", _modifiedDetailIds)}\n";
            debugMessage += $"HasChanges: {HasChanges}\n\n";

            if (detailsToAdd.Any())
            {
                debugMessage += "New Details:\n";
                foreach (var detail in detailsToAdd)
                {
                    debugMessage += $"- {detail.ProductName}: Qty={detail.Quantity}, Price={detail.PurchasePrice:C}\n";
                }
                debugMessage += "\n";
            }

            if (detailsToUpdate.Any())
            {
                debugMessage += "Modified Details:\n";
                foreach (var detail in detailsToUpdate)
                {
                    if (OriginalDetails.ContainsKey(detail.SupplierInvoiceDetailId))
                    {
                        var original = OriginalDetails[detail.SupplierInvoiceDetailId];
                        debugMessage += $"- {detail.ProductName}:\n";
                        debugMessage += $"  Qty: {original.Quantity} → {detail.Quantity}\n";
                        debugMessage += $"  Price: {original.PurchasePrice:C} → {detail.PurchasePrice:C}\n";
                        debugMessage += $"  Stock: {original.CurrentStock} → {detail.CurrentStock}\n";
                        debugMessage += $"  Storehouse: {original.Storehouse} → {detail.Storehouse}\n\n";
                    }
                    else
                    {
                        debugMessage += $"- {detail.ProductName}: (No original data found)\n";
                    }
                }
            }

            System.Windows.MessageBox.Show(debugMessage, "Debug Changes",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        private void CancelChanges()
        {
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to cancel all changes?",
                "Confirm Cancel",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _ = LoadDataAsync();
            }
        }
        private void OnDetailPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[PropertyChanged] Property '{e.PropertyName}' changed");

            if (sender is SupplierInvoiceDetailDTO detail)
            {
                System.Diagnostics.Debug.WriteLine($"[PropertyChanged] Detail: {detail.ProductName} (ID: {detail.SupplierInvoiceDetailId})");
                System.Diagnostics.Debug.WriteLine($"[PropertyChanged] Current values: Qty={detail.Quantity}, Price={detail.PurchasePrice}, Total={detail.TotalPrice}");

                // Always mark as having changes when any property changes
                HasChanges = true;

                // Handle specific property changes
                if (e.PropertyName == nameof(detail.Quantity) || e.PropertyName == nameof(detail.PurchasePrice))
                {
                    System.Diagnostics.Debug.WriteLine($"[PropertyChanged] Quantity or Price changed, TotalPrice should auto-update to: {detail.Quantity * detail.PurchasePrice}");
                }

                // Track which existing details have been modified
                if (detail.SupplierInvoiceDetailId > 0)
                {
                    if (OriginalDetails.ContainsKey(detail.SupplierInvoiceDetailId))
                    {
                        if (HasDetailChanged(detail))
                        {
                            _modifiedDetailIds.Add(detail.SupplierInvoiceDetailId);
                            System.Diagnostics.Debug.WriteLine($"[PropertyChanged] Added detail {detail.SupplierInvoiceDetailId} to modified list");
                        }
                        else
                        {
                            _modifiedDetailIds.Remove(detail.SupplierInvoiceDetailId);
                            System.Diagnostics.Debug.WriteLine($"[PropertyChanged] Removed detail {detail.SupplierInvoiceDetailId} from modified list");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[PropertyChanged] WARNING: Detail {detail.SupplierInvoiceDetailId} not found in OriginalDetails");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[PropertyChanged] New detail changed: {detail.ProductName}");
                }

                // Debug current state
                System.Diagnostics.Debug.WriteLine($"[PropertyChanged] HasChanges now: {HasChanges}");
                System.Diagnostics.Debug.WriteLine($"[PropertyChanged] Modified IDs now: [{string.Join(", ", _modifiedDetailIds)}]");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[PropertyChanged] WARNING: Sender is not SupplierInvoiceDetailDTO: {sender?.GetType().Name}");
            }
        }

        // In SupplierInvoiceProductViewModel.cs - make this method public:

        // Corrected TestPropertyChanges method - replace the existing one

        public void TestPropertyChanges()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== BASIC PROPERTY CHANGE TEST ===");

                var firstDetail = InvoiceDetails?.FirstOrDefault();
                if (firstDetail != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Testing with detail: {firstDetail.ProductName}");
                    System.Diagnostics.Debug.WriteLine($"Before change - Qty: {firstDetail.Quantity}, HasChanges: {HasChanges}");

                    // Store original value
                    var originalQty = firstDetail.Quantity;

                    // Manually trigger property change
                    firstDetail.Quantity += 0.1m;

                    System.Diagnostics.Debug.WriteLine($"After change - Qty: {firstDetail.Quantity}, HasChanges: {HasChanges}");

                    // Restore original value
                    firstDetail.Quantity = originalQty;
                    System.Diagnostics.Debug.WriteLine($"Restored to original - Qty: {firstDetail.Quantity}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No details to test with");
                }

                // Run the debug analysis
                DebugPropertyChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in TestPropertyChanges: {ex}");
            }
        }

        private void DebugPropertyChanges()
        {
            System.Diagnostics.Debug.WriteLine("=== DEBUGGING PROPERTY CHANGES ===");
            System.Diagnostics.Debug.WriteLine($"InvoiceDetails Count: {InvoiceDetails?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"HasChanges: {HasChanges}");
            System.Diagnostics.Debug.WriteLine($"Modified Detail IDs: [{string.Join(", ", _modifiedDetailIds)}]");

            if (InvoiceDetails != null)
            {
                foreach (var detail in InvoiceDetails)
                {
                    System.Diagnostics.Debug.WriteLine($"Detail: {detail.ProductName}");
                    System.Diagnostics.Debug.WriteLine($"  ID: {detail.SupplierInvoiceDetailId}");
                    System.Diagnostics.Debug.WriteLine($"  Qty: {detail.Quantity}, Price: {detail.PurchasePrice}");
                    System.Diagnostics.Debug.WriteLine($"  Total Price: {detail.TotalPrice}");
                    // Removed the PropertyChanged null check as it causes compilation error

                    if (detail.SupplierInvoiceDetailId > 0 && OriginalDetails.ContainsKey(detail.SupplierInvoiceDetailId))
                    {
                        var original = OriginalDetails[detail.SupplierInvoiceDetailId];
                        var changed = HasDetailChanged(detail);
                        System.Diagnostics.Debug.WriteLine($"  Changed: {changed}");
                        if (changed)
                        {
                            System.Diagnostics.Debug.WriteLine($"    Original Qty: {original.Quantity} -> Current: {detail.Quantity}");
                            System.Diagnostics.Debug.WriteLine($"    Original Price: {original.PurchasePrice} -> Current: {detail.PurchasePrice}");
                            System.Diagnostics.Debug.WriteLine($"    Original Stock: {original.CurrentStock} -> Current: {detail.CurrentStock}");
                            System.Diagnostics.Debug.WriteLine($"    Original Storehouse: {original.Storehouse} -> Current: {detail.Storehouse}");
                        }
                    }
                    else if (detail.SupplierInvoiceDetailId == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"  New detail (ID=0)");
                    }
                }
            }
            System.Diagnostics.Debug.WriteLine("=== END DEBUG ===");
        }
        private SupplierInvoiceDetailDTO CreateDetailCopy(SupplierInvoiceDetailDTO source)
        {
            return new SupplierInvoiceDetailDTO
            {
                SupplierInvoiceDetailId = source.SupplierInvoiceDetailId,
                SupplierInvoiceId = source.SupplierInvoiceId,
                ProductId = source.ProductId,
                ProductName = source.ProductName,
                ProductBarcode = source.ProductBarcode,
                Quantity = source.Quantity,
                PurchasePrice = source.PurchasePrice,
                TotalPrice = source.TotalPrice,
                BoxBarcode = source.BoxBarcode,
                NumberOfBoxes = source.NumberOfBoxes,
                ItemsPerBox = source.ItemsPerBox,
                BoxPurchasePrice = source.BoxPurchasePrice,
                BoxSalePrice = source.BoxSalePrice,
                CurrentStock = source.CurrentStock,
                Storehouse = source.Storehouse,
                SalePrice = source.SalePrice,
                WholesalePrice = source.WholesalePrice,
                BoxWholesalePrice = source.BoxWholesalePrice,
                MinimumStock = source.MinimumStock,
                CategoryName = source.CategoryName,
                SupplierName = source.SupplierName
            };
        }

        // Add this method to test PropertyChanged events are working

        public void TestPropertyChangeEvents()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== TESTING PROPERTY CHANGE EVENTS ===");

                bool eventFired = false;
                PropertyChangedEventHandler testHandler = (sender, e) =>
                {
                    eventFired = true;
                    System.Diagnostics.Debug.WriteLine($"TEST EVENT FIRED: {e.PropertyName} on {((SupplierInvoiceDetailDTO)sender).ProductName}");
                };

                var firstDetail = InvoiceDetails?.FirstOrDefault();
                if (firstDetail != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Testing events on: {firstDetail.ProductName}");

                    // Subscribe to the test event
                    firstDetail.PropertyChanged += testHandler;

                    // Store original values
                    var originalQty = firstDetail.Quantity;
                    var originalPrice = firstDetail.PurchasePrice;

                    System.Diagnostics.Debug.WriteLine($"Original values - Qty: {originalQty}, Price: {originalPrice}");

                    // Test quantity change
                    eventFired = false;
                    firstDetail.Quantity = originalQty + 1;
                    System.Diagnostics.Debug.WriteLine($"After Qty change - Event fired: {eventFired}, New Qty: {firstDetail.Quantity}");

                    // Test price change
                    eventFired = false;
                    firstDetail.PurchasePrice = originalPrice + 0.01m;
                    System.Diagnostics.Debug.WriteLine($"After Price change - Event fired: {eventFired}, New Price: {firstDetail.PurchasePrice}");

                    // Test if ViewModel detected changes
                    System.Diagnostics.Debug.WriteLine($"ViewModel HasChanges: {HasChanges}");
                    System.Diagnostics.Debug.WriteLine($"Modified Detail IDs: [{string.Join(", ", _modifiedDetailIds)}]");

                    // Restore original values
                    firstDetail.Quantity = originalQty;
                    firstDetail.PurchasePrice = originalPrice;

                    // Unsubscribe test handler
                    firstDetail.PropertyChanged -= testHandler;

                    System.Diagnostics.Debug.WriteLine("Test completed - values restored");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No invoice details found to test");
                }

                System.Diagnostics.Debug.WriteLine("=== END PROPERTY CHANGE TEST ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in TestPropertyChangeEvents: {ex}");
            }
        }

        private bool HasDetailChanged(SupplierInvoiceDetailDTO current)
        {
            // For new details (not yet saved), they are always considered "changed" if they exist
            if (current.SupplierInvoiceDetailId == 0)
            {
                return true; // New details are always "changed"
            }

            // For existing details, compare with original
            if (!OriginalDetails.ContainsKey(current.SupplierInvoiceDetailId))
            {
                return true; // If we don't have original data, assume it's changed
            }

            var original = OriginalDetails[current.SupplierInvoiceDetailId];

            var hasChanged = Math.Abs(current.Quantity - original.Quantity) > 0.001m ||
                             Math.Abs(current.PurchasePrice - original.PurchasePrice) > 0.001m ||
                             current.BoxBarcode != original.BoxBarcode ||
                             current.NumberOfBoxes != original.NumberOfBoxes ||
                             current.ItemsPerBox != original.ItemsPerBox ||
                             Math.Abs(current.BoxPurchasePrice - original.BoxPurchasePrice) > 0.001m ||
                             Math.Abs(current.BoxSalePrice - original.BoxSalePrice) > 0.001m ||
                             Math.Abs(current.CurrentStock - original.CurrentStock) > 0.001m ||
                             Math.Abs(current.Storehouse - original.Storehouse) > 0.001m ||
                             Math.Abs(current.SalePrice - original.SalePrice) > 0.001m ||
                             Math.Abs(current.WholesalePrice - original.WholesalePrice) > 0.001m ||
                             Math.Abs(current.BoxWholesalePrice - original.BoxWholesalePrice) > 0.001m ||
                             current.MinimumStock != original.MinimumStock;

            return hasChanged;
        }
        #endregion

        #region Cleanup

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Clean up property change event subscriptions
                CleanupPropertyChangeEvents();

                _operationLock?.Dispose();
                _searchTimer?.Dispose();
                _searchCancellationTokenSource?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}