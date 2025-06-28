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
            _supplierInvoiceService = supplierInvoiceService;
            _productService = productService;
            _categoryService = categoryService;
            _supplierService = supplierService;

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
        public ICommand AddProductCommand { get; private set; }
        public ICommand RemoveProductCommand { get; private set; }
        public ICommand SaveChangesCommand { get; private set; }
        public ICommand CancelChangesCommand { get; private set; }
        public ICommand SearchProductsCommand { get; private set; }
        public ICommand GenerateBarcodeCommand { get; private set; }
        public ICommand AddRowCommand { get; private set; }
        public ICommand UpdateNewProductCommand { get; private set; }
        public ICommand BarcodeChangedCommand { get; private set; }
        public ICommand ProductSelectedCommand { get; private set; }

        private void InitializeCommands()
        {
            LoadDataCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            AddProductCommand = new AsyncRelayCommand(async _ => await AddProductAsync());
            RemoveProductCommand = new AsyncRelayCommand(async param => await RemoveProductAsync(param));
            SaveChangesCommand = new AsyncRelayCommand(async _ => await SaveChangesAsync());
            CancelChangesCommand = new RelayCommand(_ => CancelChanges());
            SearchProductsCommand = new AsyncRelayCommand(async _ => await SearchProductsAsync());
            GenerateBarcodeCommand = new AsyncRelayCommand(async param => await GenerateBarcodeAsync(param));
            AddRowCommand = new RelayCommand(_ => AddNewRow());
            ProductSelectedCommand = new RelayCommand(param => OnProductSelected(param));
            UpdateNewProductCommand = new RelayCommand(_ => UpdateNewProductCalculations());
            BarcodeChangedCommand = new AsyncRelayCommand(async param => await OnBarcodeChanged(param));
        }

        #endregion

        #region Public Methods

        public async Task InitializeAsync(SupplierInvoiceDTO invoice)
        {
            Invoice = invoice;
            await LoadDataAsync();
        }

        #endregion

        #region Private Methods

        private async Task LoadDataAsync()
        {
            try
            {
                IsLoading = true;

                // Load reference data
                var categoriesTask = _categoryService.GetAllAsync();
                var suppliersTask = _supplierService.GetAllAsync();
                var productsTask = _productService.GetAllAsync();

                await Task.WhenAll(categoriesTask, suppliersTask, productsTask);

                Categories = new ObservableCollection<CategoryDTO>(await categoriesTask);
                Suppliers = new ObservableCollection<SupplierDTO>(await suppliersTask);
                Products = new ObservableCollection<ProductDTO>(await productsTask);

                // Load invoice details if invoice exists
                if (Invoice?.SupplierInvoiceId > 0)
                {
                    var updatedInvoice = await _supplierInvoiceService.GetByIdAsync(Invoice.SupplierInvoiceId);
                    if (updatedInvoice != null)
                    {
                        Invoice = updatedInvoice;
                        var detailsList = Invoice.Details ?? new List<SupplierInvoiceDetailDTO>();
                        InvoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>(detailsList);
                    }
                }

                InitializeNewProductRow();
            }
            catch (Exception ex)
            {
                // Handle error
                System.Windows.MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void InitializeNewProductRow()
        {
            NewProductRow = new SupplierInvoiceDetailDTO
            {
                SupplierInvoiceId = Invoice?.SupplierInvoiceId ?? 0,
                Quantity = 1,
                ItemsPerBox = 1,
                NumberOfBoxes = 1
            };
        }

        private void UpdateNewProductCalculations()
        {
            if (NewProductRow != null)
            {
                ProductValidationHelper.UpdateCalculatedFields(NewProductRow);
                HasChanges = true;
            }
        }

        private async Task OnBarcodeChanged(object parameter)
        {
            if (parameter is string barcode && !string.IsNullOrWhiteSpace(barcode))
            {
                await AutoFillProductByBarcode(barcode);
            }
        }

        private async Task AutoFillProductByBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode)) return;

            try
            {
                var product = await _productService.GetByBarcodeAsync(barcode);
                if (product != null)
                {
                    FillProductDetails(NewProductRow, product);
                }
            }
            catch (Exception ex)
            {
                // Handle error silently or show notification
                System.Diagnostics.Debug.WriteLine($"Error finding product by barcode: {ex.Message}");
            }
        }

        private void FillProductDetails(SupplierInvoiceDetailDTO detail, ProductDTO product)
        {
            detail.ProductId = product.ProductId;
            detail.ProductName = product.Name;
            detail.ProductBarcode = product.Barcode;
            detail.BoxBarcode = product.BoxBarcode ?? string.Empty;
            detail.PurchasePrice = product.PurchasePrice;
            detail.SalePrice = product.SalePrice;
            detail.WholesalePrice = product.WholesalePrice;
            detail.BoxPurchasePrice = product.BoxPurchasePrice;
            detail.BoxSalePrice = product.BoxSalePrice;
            detail.BoxWholesalePrice = product.BoxWholesalePrice;
            detail.ItemsPerBox = product.ItemsPerBox;
            detail.CurrentStock = product.CurrentStock;
            detail.Storehouse = product.Storehouse;
            detail.MinimumStock = product.MinimumStock;
            detail.CategoryName = product.CategoryName;
            detail.SupplierName = product.SupplierName;

            ProductValidationHelper.UpdateCalculatedFields(detail);
        }

        private async Task AddProductAsync()
        {
            var validationResult = ProductValidationHelper.ValidateInvoiceDetail(NewProductRow);

            if (validationResult.IsValid)
            {
                // Update calculated fields before adding
                ProductValidationHelper.UpdateCalculatedFields(NewProductRow);

                // Add the new product row to the collection
                InvoiceDetails.Add(NewProductRow);

                // Initialize a new row for the next product
                InitializeNewProductRow();

                HasChanges = true;
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Please correct the following errors:\n\n{validationResult.ErrorSummary}",
                    "Validation Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        private bool IsValidNewProduct()
        {
            var validationResult = ProductValidationHelper.ValidateInvoiceDetail(NewProductRow);
            return validationResult.IsValid;
        }

        private async Task RemoveProductAsync(object parameter)
        {
            if (parameter is SupplierInvoiceDetailDTO detail)
            {
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to remove '{detail.ProductName}' from this invoice?",
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
                    ProductValidationHelper.UpdateCalculatedFields(detail);
                }

                // Update the invoice details
                Invoice.Details = InvoiceDetails.ToList();

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
                _ = LoadDataAsync(); // Reload original data
                HasChanges = false;
            }
        }

        private async Task SearchProductsAsync()
        {
            // Implementation for product search functionality
            // This could filter the Products collection based on SearchText
        }

        private async Task GenerateBarcodeAsync(object parameter)
        {
            if (parameter is SupplierInvoiceDetailDTO detail)
            {
                // Generate a new barcode for the product
                var tempProduct = new ProductDTO
                {
                    Name = detail.ProductName,
                    CategoryId = Categories.FirstOrDefault(c => c.Name == detail.CategoryName)?.CategoryId ?? 0
                };

                var productWithBarcode = await _productService.GenerateBarcodeAsync(tempProduct);
                detail.ProductBarcode = productWithBarcode.Barcode;

                HasChanges = true;
            }
        }

        private void AddNewRow()
        {
            // Add the current new row to the collection and create a new one
            _ = AddProductAsync();
        }

        private void OnProductSelected(object parameter)
        {
            if (parameter is ProductDTO product)
            {
                FillProductDetails(NewProductRow, product);
            }
        }

        #endregion

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
    }
}