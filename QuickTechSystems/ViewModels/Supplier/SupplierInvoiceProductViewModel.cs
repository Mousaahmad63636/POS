using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
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

        private SupplierInvoiceDTO _invoice;
        private ObservableCollection<SupplierInvoiceDetailDTO> _invoiceDetails;
        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private SupplierInvoiceDetailDTO _selectedDetail;
        private SupplierInvoiceDetailDTO _newProductRow;
        private bool _isLoading;
        private string _searchText = string.Empty;
        private bool _hasChanges;

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

            // Initialize collections to prevent null reference exceptions
            _invoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>();
            _products = new ObservableCollection<ProductDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();

            InitializeCommands();
            InitializeNewProductRow();
        }

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

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
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

        #endregion

        #region Commands

        public ICommand LoadDataCommand { get; private set; }
        public ICommand SaveChangesCommand { get; private set; }
        public ICommand CancelChangesCommand { get; private set; }
        public ICommand RemoveDetailCommand { get; private set; }
        public ICommand SearchProductCommand { get; private set; }
        public ICommand SearchProductsCommand { get; private set; }
        public ICommand ScanBarcodeCommand { get; private set; }
        public ICommand AddRowCommand { get; private set; }
        public ICommand AddProductCommand { get; private set; }
        public ICommand RemoveProductCommand { get; private set; }
        public ICommand ProductSelectedCommand { get; private set; }
        public ICommand UpdateNewProductCommand { get; private set; }
        public ICommand BarcodeChangedCommand { get; private set; }
        public ICommand GenerateBarcodeCommand { get; private set; }

        private void InitializeCommands()
        {
            LoadDataCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            SaveChangesCommand = new AsyncRelayCommand(async _ => await SaveChangesAsync());
            CancelChangesCommand = new RelayCommand(_ => CancelChanges());
            RemoveDetailCommand = new RelayCommand(param => RemoveDetail(param as SupplierInvoiceDetailDTO));
            SearchProductCommand = new AsyncRelayCommand(async param => await SearchProductAsync(param));
            SearchProductsCommand = new AsyncRelayCommand(async _ => await SearchProductsAsync());
            ScanBarcodeCommand = new AsyncRelayCommand(async param => await ScanBarcodeAsync(param));
            AddRowCommand = new RelayCommand(_ => AddNewRow());
            AddProductCommand = new AsyncRelayCommand(async _ => await AddProductAsync());
            RemoveProductCommand = new AsyncRelayCommand(async param => await RemoveProductAsync(param));
            ProductSelectedCommand = new RelayCommand(param => OnProductSelected(param));
            UpdateNewProductCommand = new RelayCommand(_ => UpdateNewProductCalculations());
            BarcodeChangedCommand = new AsyncRelayCommand(async param => await OnBarcodeChanged(param));
            GenerateBarcodeCommand = new AsyncRelayCommand(async param => await GenerateBarcodeAsync(param));
        }

        #endregion

        #region Public Methods

        public async Task InitializeAsync(SupplierInvoiceDTO invoice)
        {
            try
            {
                Invoice = invoice; // Can be null for new invoices
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error initializing invoice product view: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

                // Ensure collections are initialized even if something goes wrong
                InvoiceDetails ??= new ObservableCollection<SupplierInvoiceDetailDTO>();
                Products ??= new ObservableCollection<ProductDTO>();
                Categories ??= new ObservableCollection<CategoryDTO>();
                Suppliers ??= new ObservableCollection<SupplierDTO>();

                InitializeNewProductRow();
            }
        }

        #endregion

        #region Private Methods

        private async Task LoadDataAsync()
        {
            try
            {
                IsLoading = true;

                // Load reference data with null safety
                var categoriesTask = _categoryService.GetAllAsync();
                var suppliersTask = _supplierService.GetAllAsync();
                var productsTask = _productService.GetAllAsync();

                await Task.WhenAll(categoriesTask, suppliersTask, productsTask);

                // Add null safety for service results
                var categories = await categoriesTask ?? new List<CategoryDTO>();
                var suppliers = await suppliersTask ?? new List<SupplierDTO>();
                var products = await productsTask ?? new List<ProductDTO>();

                Categories = new ObservableCollection<CategoryDTO>(categories);
                Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                Products = new ObservableCollection<ProductDTO>(products);

                // Load invoice details if invoice exists
                if (Invoice?.SupplierInvoiceId > 0)
                {
                    var updatedInvoice = await _supplierInvoiceService.GetByIdAsync(Invoice.SupplierInvoiceId);
                    if (updatedInvoice != null)
                    {
                        Invoice = updatedInvoice;
                        // Handle potential null Details collection
                        var detailsList = Invoice.Details?.ToList() ?? new List<SupplierInvoiceDetailDTO>();
                        InvoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>(detailsList);
                    }
                    else
                    {
                        // If invoice couldn't be loaded, initialize empty details
                        InvoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>();
                    }
                }
                else
                {
                    // Initialize empty details for new or invalid invoices
                    InvoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>();
                }

                InitializeNewProductRow();
            }
            catch (Exception ex)
            {
                // Handle error
                System.Windows.MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

                // Initialize empty collections to prevent further null reference exceptions
                Categories ??= new ObservableCollection<CategoryDTO>();
                Suppliers ??= new ObservableCollection<SupplierDTO>();
                Products ??= new ObservableCollection<ProductDTO>();
                InvoiceDetails ??= new ObservableCollection<SupplierInvoiceDetailDTO>();
            }
            finally
            {
                IsLoading = false;
            }
        }

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
                    ProductName = string.Empty,
                    ProductBarcode = string.Empty
                };
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error initializing new product row: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

                // Fallback initialization
                NewProductRow = new SupplierInvoiceDetailDTO();
            }
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

        private async Task SaveChangesAsync()
        {
            try
            {
                if (Invoice == null)
                {
                    System.Windows.MessageBox.Show("No invoice available to save.", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                if (InvoiceDetails == null)
                {
                    System.Windows.MessageBox.Show("No invoice details available to save.", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                // Validate all invoice details before saving
                var validationResult = ProductValidationHelper.ValidateInvoiceDetails(InvoiceDetails);

                if (!validationResult.IsValid)
                {
                    System.Windows.MessageBox.Show(
                        $"Please correct the following errors before saving:\n\n{validationResult.ErrorSummary}",
                        "Validation Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                IsLoading = true;

                // Update calculated fields for all details
                foreach (var detail in InvoiceDetails)
                {
                    if (detail != null)
                    {
                        ProductValidationHelper.UpdateCalculatedFields(detail);
                    }
                }

                // Fixed: Convert ObservableCollection to List when assigning to Invoice.Details
                Invoice.Details = new ObservableCollection<SupplierInvoiceDetailDTO>(InvoiceDetails.ToList());

                // Save through the service
                await _supplierInvoiceService.UpdateAsync(Invoice);

                // Recalculate the invoice total
                await _supplierInvoiceService.UpdateCalculatedAmountAsync(Invoice.SupplierInvoiceId);

                HasChanges = false;

                // Publish event for other ViewModels to update
                _eventAggregator.Publish(new EntityChangedEvent<SupplierInvoiceDTO>("Update", Invoice));

                System.Windows.MessageBox.Show("Changes saved successfully!", "Success",
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
            }
        }

        private void CancelChanges()
        {
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to cancel? All unsaved changes will be lost.",
                "Confirm Cancel",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // Reload the original data
                _ = LoadDataAsync();
                HasChanges = false;
            }
        }

        private async Task SearchProductAsync(object param)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
                return;

            try
            {
                var products = await _productService.SearchByNameAsync(SearchText);
                Products = new ObservableCollection<ProductDTO>(products);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error searching products: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task ScanBarcodeAsync(object param)
        {
            // Implementation for barcode scanning
            await Task.CompletedTask;
        }

        private void AddNewRow()
        {
            try
            {
                if (NewProductRow?.ProductId > 0 && InvoiceDetails != null && Invoice != null)
                {
                    var existingDetail = InvoiceDetails.FirstOrDefault(d => d.ProductId == NewProductRow.ProductId);
                    if (existingDetail != null)
                    {
                        existingDetail.Quantity += NewProductRow.Quantity;
                        existingDetail.TotalPrice = existingDetail.Quantity * existingDetail.PurchasePrice;
                    }
                    else
                    {
                        InvoiceDetails.Add(new SupplierInvoiceDetailDTO
                        {
                            SupplierInvoiceId = Invoice.SupplierInvoiceId,
                            ProductId = NewProductRow.ProductId,
                            ProductName = NewProductRow.ProductName ?? string.Empty,
                            Quantity = NewProductRow.Quantity,
                            PurchasePrice = NewProductRow.PurchasePrice,
                            TotalPrice = NewProductRow.Quantity * NewProductRow.PurchasePrice
                        });
                    }

                    InitializeNewProductRow();
                    HasChanges = true;
                }
                else
                {
                    System.Windows.MessageBox.Show("Please select a valid product before adding.", "Invalid Product",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
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
                NewProductRow.ProductId = product.ProductId;
                NewProductRow.ProductName = product.Name;
                NewProductRow.PurchasePrice = product.PurchasePrice;
                UpdateNewProductCalculations();
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
                    if (product != null)
                    {
                        OnProductSelected(product);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error finding product by barcode: {ex.Message}", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private async Task AddProductAsync()
        {
            // Delegate to AddNewRow for consistency
            AddNewRow();
            await Task.CompletedTask;
        }

        private async Task RemoveProductAsync(object param)
        {
            if (param is SupplierInvoiceDetailDTO detail)
            {
                RemoveDetail(detail);
            }
        }

        private void FillProductDetails(SupplierInvoiceDetailDTO detail, ProductDTO product)
        {
            try
            {
                if (detail != null && product != null)
                {
                    detail.ProductId = product.ProductId;
                    detail.ProductName = product.Name ?? string.Empty;
                    detail.PurchasePrice = product.PurchasePrice;
                    detail.ProductBarcode = product.Barcode ?? string.Empty;
                    UpdateNewProductCalculations();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error filling product details: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task SearchProductsAsync()
        {
            await SearchProductAsync(null);
        }

        private async Task GenerateBarcodeAsync(object parameter)
        {
            if (parameter is SupplierInvoiceDetailDTO detail)
            {
                // Generate a new barcode for the product
                var tempProduct = new ProductDTO
                {
                    Name = detail.ProductName,
                    CategoryId = Categories.FirstOrDefault()?.CategoryId ?? 0
                };

                var productWithBarcode = await _productService.GenerateBarcodeAsync(tempProduct);
                detail.ProductBarcode = productWithBarcode.Barcode;

                HasChanges = true;
            }
        }

        protected override void SubscribeToEvents()
        {
            // Subscribe to relevant events if needed
        }

        protected override void UnsubscribeFromEvents()
        {
            // Unsubscribe from events
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        #endregion
    }
}