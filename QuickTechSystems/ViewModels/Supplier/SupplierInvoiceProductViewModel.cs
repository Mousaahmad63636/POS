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
        private string _searchText = string.Empty;
        private bool _hasChanges;
        private string _newProductValidationMessage = string.Empty;
        private bool _hasNewProductValidationMessage;

        // New properties for product search functionality
        private string _productNameSearch = string.Empty;
        private ObservableCollection<ProductDTO> _searchResults;
        private bool _showSearchResults;
        private ProductDTO _selectedSearchResult;
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
        public ICommand OpenNewProductDialogCommand { get; }
        public ICommand SaveNewProductCommand { get; }
        public ICommand CancelNewProductCommand { get; }
        public ICommand GenerateBarcodeCommand { get; }
        public ICommand SelectSearchResultCommand { get; } // New command
        public ICommand ClearSearchCommand { get; } // New command

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

            // Initialize collections
            InvoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>();
            Products = new ObservableCollection<ProductDTO>();
            Categories = new ObservableCollection<CategoryDTO>();
            Suppliers = new ObservableCollection<SupplierDTO>();
            SearchResults = new ObservableCollection<ProductDTO>();

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

        #region Data Loading

        private async Task LoadDataAsync()
        {
            if (!await _operationLock.WaitAsync(0))
                return;

            try
            {
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
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
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
                            foreach (var detail in invoice.Details)
                            {
                                InvoiceDetails.Add(detail);
                            }
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
                    var newDetail = new SupplierInvoiceDetailDTO
                    {
                        SupplierInvoiceId = NewProductRow.SupplierInvoiceId,
                        ProductId = NewProductRow.ProductId,
                        ProductName = NewProductRow.ProductName,
                        ProductBarcode = NewProductRow.ProductBarcode,
                        Quantity = NewProductRow.Quantity,
                        PurchasePrice = NewProductRow.PurchasePrice,
                        TotalPrice = NewProductRow.Quantity * NewProductRow.PurchasePrice,
                        SalePrice = NewProductRow.SalePrice,
                        CurrentStock = NewProductRow.CurrentStock,
                        Storehouse = NewProductRow.Storehouse,
                        MinimumStock = NewProductRow.MinimumStock,
                        NumberOfBoxes = NewProductRow.NumberOfBoxes,
                        ItemsPerBox = NewProductRow.ItemsPerBox,
                        BoxPurchasePrice = NewProductRow.BoxPurchasePrice,
                        BoxSalePrice = NewProductRow.BoxSalePrice,
                        WholesalePrice = NewProductRow.WholesalePrice,
                        BoxWholesalePrice = NewProductRow.BoxWholesalePrice
                    };

                    InvoiceDetails.Add(newDetail);
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
                    // Create a completely new SupplierInvoiceDetailDTO instance
                    // This will force the binding to update since it's a new object reference
                    NewProductRow = new SupplierInvoiceDetailDTO
                    {
                        SupplierInvoiceId = Invoice?.SupplierInvoiceId ?? 0,
                        ProductId = product.ProductId,
                        ProductName = product.Name,
                        ProductBarcode = product.Barcode,
                        PurchasePrice = product.PurchasePrice,
                        SalePrice = product.SalePrice,
                        CurrentStock = product.CurrentStock,
                        Storehouse = product.Storehouse,
                        MinimumStock = product.MinimumStock,
                        BoxBarcode = product.BoxBarcode ?? string.Empty,
                        NumberOfBoxes = product.NumberOfBoxes,
                        ItemsPerBox = product.ItemsPerBox,
                        BoxPurchasePrice = product.BoxPurchasePrice,
                        BoxSalePrice = product.BoxSalePrice,
                        WholesalePrice = product.WholesalePrice,
                        BoxWholesalePrice = product.BoxWholesalePrice,
                        Quantity = 1, // Default quantity
                        TotalPrice = 0 // Will be calculated
                    };

                    // Update calculations
                    UpdateNewProductCalculations();

                    // Clear the search
                    ClearProductSearch();

                    // Optional: Show confirmation message
                    System.Windows.MessageBox.Show($"Product '{product.Name}' selected. Please review the details and click 'Add to Invoice'.",
                        "Product Selected", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error selecting product: {ex.Message}",
                        "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
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

        // FIXED: Enhanced barcode scanning to handle existing products properly
        private async Task OnBarcodeChanged(object param)
        {
            if (param is string barcode && !string.IsNullOrWhiteSpace(barcode))
            {
                try
                {
                    var product = await _productService.GetByBarcodeAsync(barcode);
                    if (product != null)
                    {
                        // FIXED: Instead of just calling OnProductSelected, show user options
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
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error finding product by barcode: {ex.Message}", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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

                ClearNewProductValidationMessage();

                // FIXED: Corrected parameter order - NewProductFromInvoice first, then Invoice.SupplierInvoiceId
                var createdProduct = await _supplierInvoiceService.CreateNewProductAndAddToInvoiceAsync(
                    NewProductFromInvoice, Invoice.SupplierInvoiceId);

                if (createdProduct != null)
                {
                    IsNewProductDialogOpen = false;
                    await LoadDataAsync();

                    System.Windows.MessageBox.Show("Product created and added to invoice successfully!", "Success",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                SetNewProductValidationMessage($"Error creating product: {ex.Message}");
            }
        }
        private void CancelNewProduct()
        {
            IsNewProductDialogOpen = false;
            NewProductFromInvoice = null;
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

                // Get details that need to be added (new ones)
                var detailsToAdd = InvoiceDetails.Where(d => d.SupplierInvoiceDetailId == 0).ToList();

                // Group details by whether they're for existing products or new products
                var detailsForExistingProducts = new List<SupplierInvoiceDetailDTO>();
                var detailsForNewProducts = new List<SupplierInvoiceDetailDTO>();

                foreach (var detail in detailsToAdd)
                {
                    // Check if this is an existing product (ProductId > 0 means it exists in our database)
                    if (detail.ProductId > 0)
                    {
                        var existingProduct = Products.FirstOrDefault(p => p.ProductId == detail.ProductId);
                        if (existingProduct != null)
                        {
                            detailsForExistingProducts.Add(detail);
                        }
                        else
                        {
                            detailsForNewProducts.Add(detail);
                        }
                    }
                    else
                    {
                        detailsForNewProducts.Add(detail);
                    }
                }

                // Handle existing products - update stock and average purchase price
                foreach (var detail in detailsForExistingProducts)
                {
                    try
                    {
                        await _supplierInvoiceService.AddExistingProductToInvoiceAsync(detail);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error adding existing product {detail.ProductName}: {ex.Message}", "Error",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }

                // Handle new products normally
                foreach (var detail in detailsForNewProducts)
                {
                    try
                    {
                        await _supplierInvoiceService.AddProductToInvoiceAsync(detail);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error adding new product {detail.ProductName}: {ex.Message}", "Error",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }

                await LoadDataAsync();

                System.Windows.MessageBox.Show("Changes saved successfully.", "Success",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error saving changes: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
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

        #endregion

        #region Cleanup

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _operationLock?.Dispose();
                _searchTimer?.Dispose();
                _searchCancellationTokenSource?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}