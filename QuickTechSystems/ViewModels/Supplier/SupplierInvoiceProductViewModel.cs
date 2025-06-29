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

        public NewProductFromInvoiceDTO NewProductFromInvoice
        {
            get => _newProductFromInvoice;
            set => SetProperty(ref _newProductFromInvoice, value);
        }

        public bool IsNewProductDialogOpen
        {
            get => _isNewProductDialogOpen;
            set => SetProperty(ref _isNewProductDialogOpen, value);
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
        public ICommand AddRowCommand { get; private set; }
        public ICommand AddProductCommand { get; private set; }
        public ICommand RemoveProductCommand { get; private set; }
        public ICommand ProductSelectedCommand { get; private set; }
        public ICommand BarcodeChangedCommand { get; private set; }
        public ICommand OpenNewProductDialogCommand { get; private set; }
        public ICommand SaveNewProductCommand { get; private set; }
        public ICommand CancelNewProductCommand { get; private set; }
        public ICommand GenerateBarcodeCommand { get; private set; }

        #endregion

        #region Initialization

        private void InitializeCommands()
        {
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
        }

        public async Task InitializeAsync(SupplierInvoiceDTO invoice)
        {
            if (invoice == null) return;

            Invoice = invoice;
            await LoadDataAsync();
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
                    ProductBarcode = string.Empty,
                    ItemsPerBox = 1,
                    NumberOfBoxes = 0,
                    BoxBarcode = string.Empty,
                    BoxPurchasePrice = 0,
                    BoxSalePrice = 0,
                    CurrentStock = 0,
                    Storehouse = 0,
                    SalePrice = 0,
                    WholesalePrice = 0,
                    BoxWholesalePrice = 0,
                    MinimumStock = 0
                };
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error initializing new product row: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

                NewProductRow = new SupplierInvoiceDetailDTO();
            }
        }

        #endregion

        #region Data Loading

        private async Task LoadDataAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                IsLoading = true;

                var categoriesTask = _categoryService.GetAllAsync();
                var suppliersTask = _supplierService.GetAllAsync();
                var productsTask = _productService.GetAllAsync();

                await Task.WhenAll(categoriesTask, suppliersTask, productsTask);

                var categories = await categoriesTask ?? new List<CategoryDTO>();
                var suppliers = await suppliersTask ?? new List<SupplierDTO>();
                var products = await productsTask ?? new List<ProductDTO>();

                Categories = new ObservableCollection<CategoryDTO>(categories);
                Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                Products = new ObservableCollection<ProductDTO>(products);

                if (Invoice?.SupplierInvoiceId > 0)
                {
                    var updatedInvoice = await _supplierInvoiceService.GetByIdAsync(Invoice.SupplierInvoiceId);
                    if (updatedInvoice != null)
                    {
                        Invoice = updatedInvoice;
                        var detailsList = Invoice.Details?.ToList() ?? new List<SupplierInvoiceDetailDTO>();
                        InvoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>(detailsList);
                    }
                    else
                    {
                        InvoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>();
                    }
                }
                else
                {
                    InvoiceDetails = new ObservableCollection<SupplierInvoiceDetailDTO>();
                }

                InitializeNewProductRow();
                HasChanges = false;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

                Categories ??= new ObservableCollection<CategoryDTO>();
                Suppliers ??= new ObservableCollection<SupplierDTO>();
                Products ??= new ObservableCollection<ProductDTO>();
                InvoiceDetails ??= new ObservableCollection<SupplierInvoiceDetailDTO>();
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        #endregion

        #region Product Management

        private void AddNewRow()
        {
            try
            {
                if (NewProductRow != null && NewProductRow.ProductId > 0 && NewProductRow.Quantity > 0)
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
                            SupplierInvoiceId = Invoice?.SupplierInvoiceId ?? 0,
                            ProductId = NewProductRow.ProductId,
                            ProductName = NewProductRow.ProductName,
                            ProductBarcode = NewProductRow.ProductBarcode ?? string.Empty,
                            Quantity = NewProductRow.Quantity,
                            PurchasePrice = NewProductRow.PurchasePrice,
                            TotalPrice = NewProductRow.Quantity * NewProductRow.PurchasePrice,
                            BoxBarcode = NewProductRow.BoxBarcode ?? string.Empty,
                            NumberOfBoxes = NewProductRow.NumberOfBoxes,
                            ItemsPerBox = NewProductRow.ItemsPerBox,
                            BoxPurchasePrice = NewProductRow.BoxPurchasePrice,
                            BoxSalePrice = NewProductRow.BoxSalePrice,
                            CurrentStock = NewProductRow.CurrentStock,
                            Storehouse = NewProductRow.Storehouse,
                            SalePrice = NewProductRow.SalePrice,
                            WholesalePrice = NewProductRow.WholesalePrice,
                            BoxWholesalePrice = NewProductRow.BoxWholesalePrice,
                            MinimumStock = NewProductRow.MinimumStock
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
                NewProductRow.ProductBarcode = product.Barcode;
                NewProductRow.PurchasePrice = product.PurchasePrice;
                NewProductRow.SalePrice = product.SalePrice;
                NewProductRow.CurrentStock = product.CurrentStock;
                NewProductRow.Storehouse = product.Storehouse;
                NewProductRow.MinimumStock = product.MinimumStock;
                NewProductRow.BoxBarcode = product.BoxBarcode;
                NewProductRow.NumberOfBoxes = product.NumberOfBoxes;
                NewProductRow.ItemsPerBox = product.ItemsPerBox;
                NewProductRow.BoxPurchasePrice = product.BoxPurchasePrice;
                NewProductRow.BoxSalePrice = product.BoxSalePrice;
                NewProductRow.WholesalePrice = product.WholesalePrice;
                NewProductRow.BoxWholesalePrice = product.BoxWholesalePrice;
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

        #region New Product Creation

        private async Task OpenNewProductDialogAsync()
        {
            try
            {
                if (Invoice == null)
                {
                    System.Windows.MessageBox.Show("No invoice selected to add products to.", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                NewProductFromInvoice = new NewProductFromInvoiceDTO
                {
                    SupplierId = Invoice.SupplierId,
                    SupplierName = Invoice.SupplierName,
                    IsActive = true,
                    ItemsPerBox = 1,
                    InvoiceQuantity = 1,
                    PurchasePrice = 0,
                    SalePrice = 0,
                    MinimumStock = 0,
                    MinimumBoxStock = 0,
                    CurrentStock = 0,
                    Storehouse = 0,
                    NumberOfBoxes = 0,
                    BoxPurchasePrice = 0,
                    BoxSalePrice = 0,
                    WholesalePrice = 0,
                    BoxWholesalePrice = 0
                };

                ClearNewProductValidation();
                IsNewProductDialogOpen = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening new product dialog: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task SaveNewProductAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                SetNewProductValidation("Another operation is in progress. Please wait.");
                return;
            }

            try
            {
                if (!ValidateNewProduct())
                {
                    return;
                }

                IsLoading = true;
                ClearNewProductValidation();

                var existingProduct = await _productService.GetByBarcodeAsync(NewProductFromInvoice.Barcode);
                if (existingProduct != null)
                {
                    SetNewProductValidation($"A product with barcode '{NewProductFromInvoice.Barcode}' already exists.");
                    return;
                }

                var createdProduct = await _supplierInvoiceService.CreateNewProductAndAddToInvoiceAsync(
                    NewProductFromInvoice, Invoice.SupplierInvoiceId);

                await LoadDataAsync();

                IsNewProductDialogOpen = false;
                HasChanges = false;

                System.Windows.MessageBox.Show(
                    $"Product '{createdProduct.Name}' created successfully and added to invoice.",
                    "Success",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetNewProductValidation($"Error creating product: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private void CancelNewProduct()
        {
            IsNewProductDialogOpen = false;
            NewProductFromInvoice = null;
            ClearNewProductValidation();
        }

        private bool ValidateNewProduct()
        {
            ClearNewProductValidation();

            if (NewProductFromInvoice == null)
            {
                SetNewProductValidation("Product data is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(NewProductFromInvoice.Name))
            {
                SetNewProductValidation("Product name is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(NewProductFromInvoice.Barcode))
            {
                SetNewProductValidation("Barcode is required.");
                return false;
            }

            if (NewProductFromInvoice.CategoryId <= 0)
            {
                SetNewProductValidation("Please select a category.");
                return false;
            }

            if (NewProductFromInvoice.PurchasePrice <= 0)
            {
                SetNewProductValidation("Purchase price must be greater than 0.");
                return false;
            }

            if (NewProductFromInvoice.SalePrice <= 0)
            {
                SetNewProductValidation("Sale price must be greater than 0.");
                return false;
            }

            if (NewProductFromInvoice.InvoiceQuantity <= 0)
            {
                SetNewProductValidation("Invoice quantity must be greater than 0.");
                return false;
            }

            if (NewProductFromInvoice.SupplierId <= 0)
            {
                SetNewProductValidation("Supplier information is missing.");
                return false;
            }

            if (NewProductFromInvoice.ItemsPerBox <= 0)
            {
                SetNewProductValidation("Items per box must be at least 1.");
                return false;
            }

            if (NewProductFromInvoice.NumberOfBoxes > 0)
            {
                if (NewProductFromInvoice.ItemsPerBox <= 1)
                {
                    SetNewProductValidation("For box products, items per box must be greater than 1.");
                    return false;
                }

                if (NewProductFromInvoice.BoxPurchasePrice <= 0)
                {
                    SetNewProductValidation("Box purchase price must be greater than 0 for box products.");
                    return false;
                }

                if (NewProductFromInvoice.BoxSalePrice <= 0)
                {
                    SetNewProductValidation("Box sale price must be greater than 0 for box products.");
                    return false;
                }
            }

            return true;
        }

        private void ClearNewProductValidation()
        {
            NewProductValidationMessage = string.Empty;
            HasNewProductValidationMessage = false;
        }

        private void SetNewProductValidation(string message)
        {
            NewProductValidationMessage = message;
            HasNewProductValidationMessage = !string.IsNullOrEmpty(message);
        }

        #endregion

        #region Save and Cancel

        private async Task SaveChangesAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                System.Windows.MessageBox.Show("Another save operation is in progress. Please wait.", "Info",
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

                var detailsToAdd = InvoiceDetails.Where(d => d.SupplierInvoiceDetailId == 0).ToList();

                foreach (var detail in detailsToAdd)
                {
                    try
                    {
                        await _supplierInvoiceService.AddProductToInvoiceAsync(detail);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error adding product {detail.ProductName}: {ex.Message}", "Error",
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
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}