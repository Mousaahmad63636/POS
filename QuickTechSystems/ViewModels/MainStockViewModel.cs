// Path: QuickTechSystems.WPF.ViewModels/MainStockViewModel.cs
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
using System.Threading;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.Win32;
using QuickTechSystems.Application.Services;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using System.Windows.Shapes;
using System.Windows.Media.TextFormatting;
using System.Printing;
using System.Windows.Markup;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace QuickTechSystems.WPF.ViewModels
{
    public class MainStockViewModel : ViewModelBase
    {
        private readonly IMainStockService _mainStockService;
        private readonly ICategoryService _categoryService;
        private readonly IBarcodeService _barcodeService;
        private readonly ISupplierService _supplierService;
        private readonly IImagePathService _imagePathService;
        private readonly IInventoryTransferService _inventoryTransferService;
        private readonly IProductService _productService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;
        private FlowDirection _flowDirection = FlowDirection.LeftToRight;
        private ObservableCollection<MainStockDTO> _items;
        private ObservableCollection<MainStockDTO> _filteredItems;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private ObservableCollection<ProductDTO> _storeProducts;
        private MainStockDTO? _selectedItem;
        private bool _isEditing;
        private BitmapImage? _barcodeImage;
        private string _searchText = string.Empty;
        private bool _isSaving;
        private string _statusMessage = string.Empty;
        private int _stockIncrement;
        private Dictionary<int, List<string>> _validationErrors;
        private Action<EntityChangedEvent<MainStockDTO>> _mainStockChangedHandler;
        private readonly Action<EntityChangedEvent<SupplierDTO>> _supplierChangedHandler;
        private readonly Action<EntityChangedEvent<CategoryDTO>> _categoryChangedHandler;
        private readonly Action<EntityChangedEvent<ProductDTO>> _productChangedHandler;
        private int _labelsPerProduct = 1;
        private BitmapImage? _productImage;
        private bool _isItemPopupOpen;
        private bool _isNewItem;
        private bool _isTransferPopupOpen;
        private decimal _transferQuantity = 1;
        private ProductDTO? _selectedStoreProduct;
        private decimal _totalProfit;
        private CancellationTokenSource _cts;

        // Pagination properties
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalPages;
        private ObservableCollection<int> _pageNumbers;
        private List<int> _visiblePageNumbers = new List<int>();
        private int _totalItems;
        private ObservableCollection<SupplierInvoiceDTO> _draftInvoices;
        private SupplierInvoiceDTO? _selectedInvoice;
        private string _invoiceSearchText = string.Empty;
        private readonly ISupplierInvoiceService _supplierInvoiceService;

        public ObservableCollection<SupplierInvoiceDTO> DraftInvoices
        {
            get => _draftInvoices;
            set => SetProperty(ref _draftInvoices, value);
        }

        public SupplierInvoiceDTO? SelectedInvoice
        {
            get => _selectedInvoice;
            set => SetProperty(ref _selectedInvoice, value);
        }

        public string InvoiceSearchText
        {
            get => _invoiceSearchText;
            set
            {
                if (SetProperty(ref _invoiceSearchText, value))
                {
                    _ = LoadDraftInvoicesAsync();
                }
            }
        }

        public FlowDirection CurrentFlowDirection
        {
            get => _flowDirection;
            set => SetProperty(ref _flowDirection, value);
        }

        public ObservableCollection<MainStockDTO> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        public ObservableCollection<MainStockDTO> FilteredItems
        {
            get => _filteredItems;
            set => SetProperty(ref _filteredItems, value);
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

        public ObservableCollection<ProductDTO> StoreProducts
        {
            get => _storeProducts;
            set => SetProperty(ref _storeProducts, value);
        }

        public MainStockDTO? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != null)
                {
                    // Unsubscribe from property changes on the old selected item
                    _selectedItem.PropertyChanged -= SelectedItem_PropertyChanged;
                }

                SetProperty(ref _selectedItem, value);
                IsEditing = value != null;

                if (value != null)
                {
                    // Subscribe to property changes on the new selected item
                    value.PropertyChanged += SelectedItem_PropertyChanged;

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
                }
                else
                {
                    BarcodeImage = null;
                    ProductImage = null;
                }
            }
        }

        public ProductDTO? SelectedStoreProduct
        {
            get => _selectedStoreProduct;
            set => SetProperty(ref _selectedStoreProduct, value);
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

        public BitmapImage? ProductImage
        {
            get => _productImage;
            set => SetProperty(ref _productImage, value);
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
                    FilterItems();
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

        public decimal TransferQuantity
        {
            get => _transferQuantity;
            set => SetProperty(ref _transferQuantity, value);
        }

        public Dictionary<int, List<string>> ValidationErrors
        {
            get => _validationErrors;
            set => SetProperty(ref _validationErrors, value);
        }

        public int LabelsPerProduct
        {
            get => _labelsPerProduct;
            set => SetProperty(ref _labelsPerProduct, Math.Max(1, value));
        }

        public bool IsItemPopupOpen
        {
            get => _isItemPopupOpen;
            set => SetProperty(ref _isItemPopupOpen, value);
        }

        public bool IsTransferPopupOpen
        {
            get => _isTransferPopupOpen;
            set => SetProperty(ref _isTransferPopupOpen, value);
        }

        public bool IsNewItem
        {
            get => _isNewItem;
            set => SetProperty(ref _isNewItem, value);
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

        public int TotalItems
        {
            get => _totalItems;
            private set => SetProperty(ref _totalItems, value);
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
        public ICommand TransferToStoreCommand { get; private set; }
        public ICommand SaveTransferCommand { get; private set; }

        // Pagination commands
        public ICommand NextPageCommand { get; private set; }
        public ICommand PreviousPageCommand { get; private set; }
        public ICommand GoToPageCommand { get; private set; }
        public ICommand ChangePageSizeCommand { get; private set; }

        public MainStockViewModel(
            IMainStockService mainStockService,
            ICategoryService categoryService,
            IBarcodeService barcodeService,
            ISupplierService supplierService,
            ISupplierInvoiceService supplierInvoiceService,
            IImagePathService imagePathService,
            IInventoryTransferService inventoryTransferService,
            IProductService productService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            Debug.WriteLine("Initializing MainStockViewModel");
            _mainStockService = mainStockService ?? throw new ArgumentNullException(nameof(mainStockService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _barcodeService = barcodeService ?? throw new ArgumentNullException(nameof(barcodeService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));
            _imagePathService = imagePathService ?? throw new ArgumentNullException(nameof(imagePathService));
            _supplierInvoiceService = supplierInvoiceService ?? throw new ArgumentNullException(nameof(supplierInvoiceService));
            _inventoryTransferService = inventoryTransferService ?? throw new ArgumentNullException(nameof(inventoryTransferService));
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));

            _items = new ObservableCollection<MainStockDTO>();
            _filteredItems = new ObservableCollection<MainStockDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();
            _storeProducts = new ObservableCollection<ProductDTO>();
            _draftInvoices = new ObservableCollection<SupplierInvoiceDTO>();
            _validationErrors = new Dictionary<int, List<string>>();
            _mainStockChangedHandler = HandleMainStockChanged;
            _categoryChangedHandler = HandleCategoryChanged;
            _supplierChangedHandler = HandleSupplierChanged;
            _productChangedHandler = HandleProductChanged;
            _pageNumbers = new ObservableCollection<int>();
            _cts = new CancellationTokenSource();

            SubscribeToEvents();
            InitializeCommands();
            _ = LoadDataAsync();
            Debug.WriteLine("MainStockViewModel initialized");
        }

        private async Task LoadDraftInvoicesAsync()
        {
            try
            {
                var invoices = await _supplierInvoiceService.GetByStatusAsync("Draft");

                // Filter by search text if provided
                if (!string.IsNullOrWhiteSpace(InvoiceSearchText))
                {
                    var searchText = InvoiceSearchText.ToLower();
                    invoices = invoices.Where(i =>
                        i.InvoiceNumber.ToLower().Contains(searchText) ||
                        i.SupplierName.ToLower().Contains(searchText)
                    ).ToList();
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DraftInvoices = new ObservableCollection<SupplierInvoiceDTO>(invoices);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading draft invoices: {ex.Message}");
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
            TransferToStoreCommand = new RelayCommand(_ => ShowTransferDialog(), _ => !IsSaving);
            SaveTransferCommand = new AsyncRelayCommand(async _ =>
            {
                try
                {
                    // Call the instance method for saving transfers
                    await TransferToStoreAsync();
                    // No need to check result since the method doesn't return anything
                    IsTransferPopupOpen = false;
                    await SafeLoadDataAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in transfer: {ex.Message}");
                    ShowTemporaryErrorMessage($"Transfer failed: {ex.Message}");
                }
            }, _ => !IsSaving);
            // Pagination commands
            NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => !IsLastPage);
            PreviousPageCommand = new RelayCommand(_ => CurrentPage--, _ => !IsFirstPage);
            GoToPageCommand = new RelayCommand<int>(page => CurrentPage = page);
            ChangePageSizeCommand = new RelayCommand<int>(size => PageSize = size);
        }

        protected override void SubscribeToEvents()
        {
            Debug.WriteLine("MainStockViewModel: Subscribing to events");
            _eventAggregator.Subscribe<EntityChangedEvent<MainStockDTO>>(_mainStockChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<SupplierDTO>>(_supplierChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
            Debug.WriteLine("MainStockViewModel: Subscribed to all events");
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<MainStockDTO>>(_mainStockChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<SupplierDTO>>(_supplierChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
        }

        private void SelectedItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // You can add any property change handling if needed
        }

        private async void HandleMainStockChanged(EntityChangedEvent<MainStockDTO> evt)
        {
            try
            {
                Debug.WriteLine($"MainStockViewModel: Handling MainStock change: {evt.Action}");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    switch (evt.Action)
                    {
                        case "Create":
                            if (!Items.Any(p => p.MainStockId == evt.Entity.MainStockId))
                            {
                                Items.Add(evt.Entity);
                                Debug.WriteLine($"Added new MainStock item {evt.Entity.Name}");
                            }
                            break;

                        case "Update":
                            var existingIndex = Items.ToList().FindIndex(p => p.MainStockId == evt.Entity.MainStockId);
                            if (existingIndex != -1)
                            {
                                Items[existingIndex] = evt.Entity;
                                Debug.WriteLine($"Updated MainStock item {evt.Entity.Name}");
                            }
                            break;

                        case "Delete":
                            var itemToRemove = Items.FirstOrDefault(p => p.MainStockId == evt.Entity.MainStockId);
                            if (itemToRemove != null)
                            {
                                Items.Remove(itemToRemove);
                                Debug.WriteLine($"Removed MainStock item {itemToRemove.Name}");
                            }
                            break;
                    }

                    // Refresh filtered items if we're using search
                    if (!string.IsNullOrWhiteSpace(SearchText))
                    {
                        FilterItems();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainStock refresh error: {ex.Message}");
            }
        }

        private async void HandleCategoryChanged(EntityChangedEvent<CategoryDTO> evt)
        {
            try
            {
                Debug.WriteLine("MainStockViewModel: Handling category change");
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
                Debug.WriteLine($"MainStockViewModel: Error handling category change: {ex.Message}");
            }
        }

        private async void HandleSupplierChanged(EntityChangedEvent<SupplierDTO> evt)
        {
            try
            {
                Debug.WriteLine($"MainStockViewModel: Handling supplier change: {evt.Action}");
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
                Debug.WriteLine($"MainStockViewModel: Error handling supplier change: {ex.Message}");
            }
        }

        private async void HandleProductChanged(EntityChangedEvent<ProductDTO> evt)
        {
            try
            {
                Debug.WriteLine($"MainStockViewModel: Handling Product change: {evt.Action}");
                await LoadStoreProductsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainStockViewModel: Error handling product change: {ex.Message}");
            }
        }

        private void ShowTransferDialog()
        {
            if (SelectedItem == null)
            {
                ShowTemporaryErrorMessage("Please select an item to transfer.");
                return;
            }

            // Reset transfer quantity
            TransferQuantity = 1;

            // Refresh store products list
            _ = LoadStoreProductsAsync();

            // Show transfer dialog
            IsTransferPopupOpen = true;
        }

        private async Task LoadStoreProductsAsync()
        {
            try
            {
                var products = await _productService.GetAllAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StoreProducts = new ObservableCollection<ProductDTO>(products);

                    // If we have a selected item, try to find a matching store product
                    if (SelectedItem != null)
                    {
                        var matchingProduct = StoreProducts.FirstOrDefault(p =>
                            p.Barcode == SelectedItem.Barcode ||
                            p.Name == SelectedItem.Name);

                        if (matchingProduct != null)
                        {
                            SelectedStoreProduct = matchingProduct;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading store products: {ex.Message}");
            }
        }

        private async Task TransferToStoreAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Transfer operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (SelectedItem == null)
                {
                    ShowTemporaryErrorMessage("Please select an item to transfer.");
                    return;
                }

                if (SelectedStoreProduct == null)
                {
                    ShowTemporaryErrorMessage("Please select a store product to transfer to.");
                    return;
                }

                if (TransferQuantity <= 0)
                {
                    ShowTemporaryErrorMessage("Transfer quantity must be greater than zero.");
                    return;
                }

                if (TransferQuantity > SelectedItem.CurrentStock)
                {
                    ShowTemporaryErrorMessage($"Transfer quantity ({TransferQuantity}) exceeds available stock ({SelectedItem.CurrentStock}).");
                    return;
                }

                IsSaving = true;
                StatusMessage = "Processing transfer...";

                // Get the current user for the transfer record
                string transferredBy = "System User"; // You might want to get the actual user name from your app

                try
                {
                    // Make sure SelectedItem and SelectedStoreProduct still have their IDs
                    Debug.WriteLine($"Transfer details: MainStock ID: {SelectedItem.MainStockId}, Product ID: {SelectedStoreProduct.ProductId}, Quantity: {TransferQuantity}");

                    // Ensure the product isn't referencing this MainStock item already
                    if (SelectedStoreProduct.MainStockId.HasValue && SelectedStoreProduct.MainStockId.Value == SelectedItem.MainStockId)
                    {
                        // Temporarily clear the reference to avoid circular dependency
                        var productToUpdate = await _productService.GetByIdAsync(SelectedStoreProduct.ProductId);
                        if (productToUpdate != null)
                        {
                            productToUpdate.MainStockId = null;
                            await _productService.UpdateAsync(productToUpdate);
                        }
                    }

                    bool result = await _mainStockService.TransferToStoreAsync(
                        SelectedItem.MainStockId,
                        SelectedStoreProduct.ProductId,
                        TransferQuantity,
                        transferredBy,
                        $"Manual transfer from MainStock to Store"
                    );

                    if (result)
                    {
                        // No need to close the popup here, it will be closed by the command handler

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(
                                $"Successfully transferred {TransferQuantity} units from MainStock to Store inventory.",
                                "Transfer Successful",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                        });

                        // Refresh the data to show updated quantities - handled by the command
                    }
                    else
                    {
                        ShowTemporaryErrorMessage("Transfer failed. Please try again.");
                    }
                }
                catch (Exception ex)
                {
                    // Provide more detailed error information
                    var errorMessage = $"Error processing transfer: {ex.Message}";
                    if (ex.InnerException != null)
                    {
                        errorMessage += $"\nDetails: {ex.InnerException.Message}";
                    }

                    Debug.WriteLine($"Transfer error details: {ex}");
                    ShowTemporaryErrorMessage(errorMessage);
                }
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
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

        public void ShowItemPopup()
        {
            try
            {
                // Create a new instance of the MainStockDetailsWindow
                var itemWindow = new MainStockDetailsWindow
                {
                    DataContext = this,
                    Owner = GetOwnerWindow()
                };

                // Subscribe to save completed event
                itemWindow.SaveCompleted += ItemDetailsWindow_SaveCompleted;

                // Show the window as dialog
                itemWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing MainStock window: {ex.Message}");
                ShowTemporaryErrorMessage($"Error displaying item details: {ex.Message}");
            }
        }

        private void ItemDetailsWindow_SaveCompleted(object sender, RoutedEventArgs e)
        {
            // When save is completed, close the window
            // The window itself already handles closing through the DialogResult
        }

        public void CloseItemPopup()
        {
            // This is no longer needed with Window approach, as the window handles its own closing
            // But we'll keep it for backward compatibility
            IsItemPopupOpen = false;
        }

        public void EditItem(MainStockDTO item)
        {
            if (item != null)
            {
                SelectedItem = item;
                IsNewItem = false;
                ShowItemPopup();
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
                    if (System.IO.Path.IsPathRooted(imagePath))
                    {
                        fullPath = imagePath;
                    }
                    else
                    {
                        fullPath = System.IO.Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "ProductImages",
                            imagePath
                        );
                    }
                }

                if (!System.IO.File.Exists(fullPath))
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
                    else if (System.IO.Path.IsPathRooted(imagePath))
                    {
                        retryPath = imagePath;
                    }
                    else
                    {
                        retryPath = System.IO.Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "ProductImages",
                            imagePath
                        );
                    }

                    // Fallback to stream-based loading with correct parameters
                    BitmapImage fallbackImage = new BitmapImage();

                    // Use using statement with proper FileStream parameters
                    using (var stream = new System.IO.FileStream(retryPath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
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
        private void UploadImage()
        {
            // Store current popup state
            bool wasPopupOpen = IsItemPopupOpen;

            // Temporarily close the popup
            if (wasPopupOpen)
            {
                IsItemPopupOpen = false;
            }

            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Image files (*.jpg, *.jpeg, *.png) | *.jpg; *.jpeg; *.png",
                    Title = "Select image"
                };

                // Get the current owner window
                var ownerWindow = GetOwnerWindow();

                // Show dialog with proper owner
                bool? result = openFileDialog.ShowDialog(ownerWindow);

                if (result == true && SelectedItem != null)
                {
                    try
                    {
                        // Get the source path
                        string sourcePath = openFileDialog.FileName;

                        // Save the image and get the relative path
                        string savedPath = _imagePathService.SaveProductImage(sourcePath);

                        // Set the ImagePath property
                        SelectedItem.ImagePath = savedPath;

                        // Load the image
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
                    IsItemPopupOpen = true;
                }
            }
        }

        private void ClearImage()
        {
            if (SelectedItem != null)
            {
                // If there's an existing image, attempt to delete the file
                if (!string.IsNullOrEmpty(SelectedItem.ImagePath))
                {
                    _imagePathService.DeleteProductImage(SelectedItem.ImagePath);
                }

                SelectedItem.ImagePath = null;
                ProductImage = null;
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
                    var storeProductsTask = _productService.GetAllAsync();

                    // Get total count of items
                    var mainStockItems = await _mainStockService.GetAllAsync();
                    if (linkedCts.Token.IsCancellationRequested) return;

                    // Calculate total pages
                    var filteredItems = FilterMainStockItems(mainStockItems, SearchText);
                    int totalCount = filteredItems.Count();
                    int calculatedTotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
                    TotalPages = calculatedTotalPages;
                    TotalItems = totalCount;

                    // Apply pagination to filtered items
                    var pagedItems = filteredItems
                        .Skip((CurrentPage - 1) * PageSize)
                        .Take(PageSize)
                        .ToList();

                    if (linkedCts.Token.IsCancellationRequested) return;

                    // Wait for categories and suppliers to complete
                    await Task.WhenAll(categoriesTask, suppliersTask, storeProductsTask);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    var categories = await categoriesTask;
                    var suppliers = await suppliersTask;
                    var storeProducts = await storeProductsTask;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!linkedCts.Token.IsCancellationRequested)
                        {
                            Items = new ObservableCollection<MainStockDTO>(pagedItems);
                            FilteredItems = new ObservableCollection<MainStockDTO>(pagedItems);
                            Categories = new ObservableCollection<CategoryDTO>(categories);
                            Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                            StoreProducts = new ObservableCollection<ProductDTO>(storeProducts);
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

        private IEnumerable<MainStockDTO> FilterMainStockItems(IEnumerable<MainStockDTO> items, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return items;

            searchText = searchText.ToLower();
            return items.Where(i =>
                i.Name.ToLower().Contains(searchText) ||
                i.Barcode.ToLower().Contains(searchText) ||
                i.CategoryName.ToLower().Contains(searchText) ||
                i.SupplierName.ToLower().Contains(searchText) ||
                (i.Description?.ToLower().Contains(searchText) ?? false)
            );
        }

        protected override async Task LoadDataAsync()
        {
            await SafeLoadDataAsync();
        }

        private void AddNew()
        {
            SelectedItem = new MainStockDTO
            {
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            BarcodeImage = null;
            ValidationErrors.Clear();
            IsNewItem = true;

            // Load draft invoices for selection
            _ = LoadDraftInvoicesAsync();

            ShowItemPopup();
        }

        private void FilterItems()
        {
            _ = SafeLoadDataAsync();
        }

        private async Task SaveAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Save operation already in progress. Please wait.");
                return;
            }

            try
            {
                Debug.WriteLine("Starting save operation");
                if (SelectedItem == null) return;

                IsSaving = true;
                StatusMessage = "Validating item...";

                // Create a complete copy of the selected item to avoid tracking issues
                var itemToUpdate = new MainStockDTO
                {
                    MainStockId = SelectedItem.MainStockId,
                    Name = SelectedItem.Name,
                    Barcode = SelectedItem.Barcode,
                    CategoryId = SelectedItem.CategoryId,
                    CategoryName = SelectedItem.CategoryName,
                    SupplierId = SelectedItem.SupplierId,
                    SupplierName = SelectedItem.SupplierName,
                    Description = SelectedItem.Description,
                    PurchasePrice = SelectedItem.PurchasePrice,
                    SalePrice = SelectedItem.SalePrice,
                    CurrentStock = SelectedItem.CurrentStock,
                    MinimumStock = SelectedItem.MinimumStock,
                    BarcodeImage = SelectedItem.BarcodeImage,
                    Speed = SelectedItem.Speed,
                    IsActive = SelectedItem.IsActive,
                    ImagePath = SelectedItem.ImagePath,
                    CreatedAt = SelectedItem.CreatedAt,
                    UpdatedAt = DateTime.Now
                };

                // Check if barcode is empty and generate one if needed
                if (string.IsNullOrWhiteSpace(itemToUpdate.Barcode))
                {
                    Debug.WriteLine("No barcode provided, generating automatic barcode");

                    // Generate a unique barcode based on category, timestamp, and random number
                    var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8); // Use ticks for uniqueness
                    var random = new Random();
                    var randomDigits = random.Next(1000, 9999).ToString();
                    var categoryPrefix = itemToUpdate.CategoryId.ToString().PadLeft(3, '0');

                    itemToUpdate.Barcode = $"{categoryPrefix}-{timestamp}-{randomDigits}";
                }

                // Always ensure barcode image exists before saving
                if (itemToUpdate.BarcodeImage == null && !string.IsNullOrWhiteSpace(itemToUpdate.Barcode))
                {
                    Debug.WriteLine("Generating barcode image for item");
                    try
                    {
                        itemToUpdate.BarcodeImage = _barcodeService.GenerateBarcode(itemToUpdate.Barcode);

                        // Update the UI image
                        BarcodeImage = LoadBarcodeImage(itemToUpdate.BarcodeImage);
                        Debug.WriteLine("Barcode image generated successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                        // Continue despite this error - we can still save without the image
                    }
                }

                if (!ValidateItem(itemToUpdate))
                {
                    return;
                }

                // Check for duplicate barcode
                try
                {
                    var existingItem = await _mainStockService.FindProductByBarcodeAsync(
                        itemToUpdate.Barcode,
                        itemToUpdate.MainStockId);

                    if (existingItem != null)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(
                                $"Cannot save item: An item with barcode '{existingItem.Barcode}' already exists: '{existingItem.Name}'.",
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

                StatusMessage = "Saving item...";

                try
                {
                    MainStockDTO savedItem;

                    if (itemToUpdate.MainStockId == 0)
                    {
                        // Create new item
                        savedItem = await _mainStockService.CreateAsync(itemToUpdate);
                    }
                    else
                    {
                        // Update existing item - use the UpdateAsync method
                        savedItem = await _mainStockService.UpdateAsync(itemToUpdate);

                    }

                    // Update SelectedItem reference with the returned values
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        SelectedItem = savedItem;
                    });

                    // Handle supplier invoice association if selected
                    if (SelectedInvoice != null && savedItem != null)
                    {
                        try
                        {
                            StatusMessage = "Adding product to invoice...";

                            // Find or create a corresponding Product for this MainStock item
                            var matchingProduct = await _productService.FindProductByBarcodeAsync(savedItem.Barcode);

                            // If no matching product exists, create one based on this MainStock item
                            if (matchingProduct == null)
                            {
                                var newProduct = new ProductDTO
                                {
                                    Name = savedItem.Name,
                                    Barcode = savedItem.Barcode,
                                    CategoryId = savedItem.CategoryId,
                                    CategoryName = savedItem.CategoryName,
                                    SupplierId = savedItem.SupplierId,
                                    SupplierName = savedItem.SupplierName,
                                    Description = savedItem.Description,
                                    PurchasePrice = savedItem.PurchasePrice,
                                    SalePrice = savedItem.SalePrice,
                                    CurrentStock = 0, // Start with zero stock
                                    MinimumStock = savedItem.MinimumStock,
                                    ImagePath = savedItem.ImagePath,
                                    Speed = savedItem.Speed,
                                    IsActive = savedItem.IsActive,
                                };

                                matchingProduct = await _productService.CreateAsync(newProduct);
                            }

                            // Get the quantity from the product's current stock
                            decimal quantity = savedItem.CurrentStock;

                            var invoiceDetail = new SupplierInvoiceDetailDTO
                            {
                                SupplierInvoiceId = SelectedInvoice.SupplierInvoiceId,
                                ProductId = matchingProduct.ProductId, // Use the real Product ID
                                ProductName = savedItem.Name,
                                ProductBarcode = savedItem.Barcode,
                                Quantity = quantity,
                                PurchasePrice = savedItem.PurchasePrice,
                                TotalPrice = savedItem.PurchasePrice * quantity // Calculate total correctly
                            };

                            await _supplierInvoiceService.AddProductToInvoiceAsync(invoiceDetail);

                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show($"Product saved and added to invoice {SelectedInvoice.InvoiceNumber} with quantity {quantity}.", "Success",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error adding product to invoice: {ex.Message}");

                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show($"Product saved successfully but could not be added to invoice: {ex.Message}",
                                    "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                            });
                        }
                    }
                    else
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show("Product saved successfully.", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }

                    CloseItemPopup();

                    // Refresh the data
                    await SafeLoadDataAsync();

                    Debug.WriteLine("Save completed, item refreshed");
                }
                catch (Exception ex)
                {
                    var errorMessage = GetDetailedErrorMessage(ex);
                    Debug.WriteLine($"Save error: {errorMessage}");
                    ShowTemporaryErrorMessage($"Error saving item: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in save operation: {ex.Message}");
                ShowTemporaryErrorMessage($"Error saving item: {ex.Message}");
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

        private bool ValidateItem(MainStockDTO item)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(item.Name))
                errors.Add("Item name is required");

            if (item.CategoryId <= 0)
                errors.Add("Please select a category");

            if (item.SalePrice <= 0)
                errors.Add("Sale price must be greater than zero");

            // Modified validation: allows purchase price of 0 but prevents negative values
            if (item.PurchasePrice < 0)
                errors.Add("Purchase price cannot be negative");

            if (item.MinimumStock < 0)
                errors.Add("Minimum stock cannot be negative");

            if (!string.IsNullOrWhiteSpace(item.Speed))
            {
                if (!decimal.TryParse(item.Speed, out _))
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
                if (SelectedItem == null) return;

                var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    return MessageBox.Show($"Are you sure you want to delete {SelectedItem.Name}?",
                        "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                });

                if (result == MessageBoxResult.Yes)
                {
                    IsSaving = true;
                    StatusMessage = "Deleting item...";

                    var itemId = SelectedItem.MainStockId;
                    var itemName = SelectedItem.Name;

                    try
                    {
                        await _mainStockService.DeleteAsync(itemId);

                        // Remove from the local collection
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var itemToRemove = Items.FirstOrDefault(p => p.MainStockId == itemId);
                            if (itemToRemove != null)
                            {
                                Items.Remove(itemToRemove);
                            }
                        });

                        // Close popup if it's open
                        if (IsItemPopupOpen)
                        {
                            CloseItemPopup();
                        }

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show($"Item '{itemName}' has been deleted successfully.",
                                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        });

                        // Clear the selected item
                        SelectedItem = null;

                        // Refresh the data
                        await SafeLoadDataAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error deleting item {itemId}: {ex.Message}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error deleting item: {ex.Message}");
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
            if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.Barcode))
            {
                ShowTemporaryErrorMessage("Please enter a barcode value first.");
                return;
            }

            try
            {
                var barcodeData = _barcodeService.GenerateBarcode(SelectedItem.Barcode);
                if (barcodeData != null)
                {
                    SelectedItem.BarcodeImage = barcodeData;
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
            if (SelectedItem == null)
            {
                ShowTemporaryErrorMessage("Please select an item first.");
                return;
            }

            try
            {
                var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                var random = new Random();
                var randomDigits = random.Next(1000, 9999).ToString();
                var categoryPrefix = SelectedItem.CategoryId.ToString().PadLeft(3, '0');

                SelectedItem.Barcode = $"{categoryPrefix}{timestamp}{randomDigits}";
                var barcodeData = _barcodeService.GenerateBarcode(SelectedItem.Barcode);

                if (barcodeData != null)
                {
                    SelectedItem.BarcodeImage = barcodeData;
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

        private async Task UpdateStockAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Stock update operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (SelectedItem == null || StockIncrement <= 0)
                {
                    ShowTemporaryErrorMessage("Please select an item and enter a valid stock increment.");
                    return;
                }

                IsSaving = true;
                StatusMessage = "Updating stock...";

                var newStock = SelectedItem.CurrentStock + StockIncrement;
                SelectedItem.CurrentStock = newStock;

                await _mainStockService.UpdateStockAsync(SelectedItem.MainStockId, StockIncrement);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Stock updated successfully. New stock: {newStock}",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                });

                StockIncrement = 0;
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
                if (SelectedItem == null)
                {
                    ShowTemporaryErrorMessage("Please select an item first.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedItem.Barcode))
                {
                    ShowTemporaryErrorMessage("This item does not have a barcode assigned.");
                    return;
                }

                StatusMessage = "Preparing barcode...";
                IsSaving = true;

                // Generate barcode image if needed
                if (SelectedItem.BarcodeImage == null)
                {
                    try
                    {
                        var barcodeData = _barcodeService.GenerateBarcode(SelectedItem.Barcode, 600, 200);
                        if (barcodeData == null)
                        {
                            ShowTemporaryErrorMessage("Failed to generate barcode.");
                            return;
                        }

                        SelectedItem.BarcodeImage = barcodeData;
                        BarcodeImage = LoadBarcodeImage(barcodeData);

                        // Save updated item with barcode image
                        var itemCopy = new MainStockDTO
                        {
                            MainStockId = SelectedItem.MainStockId,
                            Name = SelectedItem.Name,
                            Barcode = SelectedItem.Barcode,
                            CategoryId = SelectedItem.CategoryId,
                            CategoryName = SelectedItem.CategoryName,
                            SupplierId = SelectedItem.SupplierId,
                            SupplierName = SelectedItem.SupplierName,
                            Description = SelectedItem.Description,
                            PurchasePrice = SelectedItem.PurchasePrice,
                            SalePrice = SelectedItem.SalePrice,
                            CurrentStock = SelectedItem.CurrentStock,
                            MinimumStock = SelectedItem.MinimumStock,
                            BarcodeImage = barcodeData,
                            Speed = SelectedItem.Speed,
                            IsActive = SelectedItem.IsActive,
                            ImagePath = SelectedItem.ImagePath,
                            CreatedAt = SelectedItem.CreatedAt,
                            UpdatedAt = DateTime.Now
                        };

                        await _mainStockService.UpdateAsync(itemCopy);
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

                        StatusMessage = $"Creating {LabelsPerProduct} labels...";

                        // Calculate how many labels can fit on a page
                        double pageWidth = printDialog.PrintableAreaWidth;
                        double pageHeight = printDialog.PrintableAreaHeight;
                        double labelWidth = 280; // Smaller label width
                        double labelHeight = 140; // Smaller label height

                        // Calculate columns and rows
                        int columns = Math.Max(1, (int)(pageWidth / labelWidth));
                        int labelsPerPage = columns * (int)(pageHeight / labelHeight);

                        // Create a wrapping panel for better label layout
                        var wrapPanel = new WrapPanel
                        {
                            Width = pageWidth,
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };

                        // Create labels
                        for (int i = 0; i < LabelsPerProduct; i++)
                        {
                            var labelVisual = CreatePrintVisual(SelectedItem);
                            wrapPanel.Children.Add(labelVisual);
                        }

                        // Print the grid containing the labels
                        StatusMessage = "Sending to printer...";
                        printDialog.PrintVisual(wrapPanel, $"Barcode - {SelectedItem.Name}");

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
        private UIElement CreatePrintVisual(MainStockDTO item)
        {
            // Create a container for the label content
            var canvas = new Canvas
            {
                Width = 280, // Reduced from 380
                Height = 140, // Reduced from 220
                Background = Brushes.White,
                Margin = new Thickness(2) // Smaller margin
            };

            try
            {
                // Add item name (with null check)
                var nameText = item.Name ?? "Unknown Item";
                var nameTextBlock = new TextBlock
                {
                    Text = nameText,
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 9, // Reduced from 12
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Width = 260, // Reduced from 360
                    MaxHeight = 25 // Reduced from 40
                };

                // Position product name at top
                Canvas.SetLeft(nameTextBlock, 10);
                Canvas.SetTop(nameTextBlock, 5); // Reduced from 10
                canvas.Children.Add(nameTextBlock);

                // Position the barcode image - use most of the available space
                double barcodeWidth = 240; // Reduced from 340
                double barcodeHeight = 70; // Reduced from 100

                // Load barcode image with null check
                BitmapImage bitmapSource = null;
                if (item.BarcodeImage != null)
                {
                    bitmapSource = LoadBarcodeImage(item.BarcodeImage);
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
                        FontSize = 8, // Reduced from 12
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    };

                    placeholder.Child = placeholderText;

                    // Position placeholder
                    Canvas.SetLeft(placeholder, 20);
                    Canvas.SetTop(placeholder, 35); // Reduced from 55
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

                    // Set high-quality rendering options if available
                    RenderOptions.SetBitmapScalingMode(barcodeImage, BitmapScalingMode.HighQuality);
                    RenderOptions.SetEdgeMode(barcodeImage, EdgeMode.Aliased);

                    Canvas.SetLeft(barcodeImage, 20);
                    Canvas.SetTop(barcodeImage, 35); // Reduced from 55
                    canvas.Children.Add(barcodeImage);
                }

                // Add barcode text (with null check)
                var barcodeText = item.Barcode ?? "No Barcode";
                var barcodeTextBlock = new TextBlock
                {
                    Text = barcodeText,
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 8, // Reduced from 11
                    TextAlignment = TextAlignment.Center,
                    Width = 260 // Reduced from 360
                };

                // Position barcode text below where the barcode image would be
                Canvas.SetLeft(barcodeTextBlock, 10);
                Canvas.SetTop(barcodeTextBlock, 110); // Reduced from 160
                canvas.Children.Add(barcodeTextBlock);

                // Add price if needed
                if (item.SalePrice > 0)
                {
                    var priceTextBlock = new TextBlock
                    {
                        Text = $"${item.SalePrice:N2}",
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 10, // Reduced from 14
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        Width = 260 // Reduced from 360
                    };

                    // Position price at bottom
                    Canvas.SetLeft(priceTextBlock, 10);
                    Canvas.SetTop(priceTextBlock, 125); // Reduced from 185
                    canvas.Children.Add(priceTextBlock);
                }

                // Add a border around the entire label for visual separation
                var border = new Border
                {
                    Width = 280, // Reduced from 380
                    Height = 140, // Reduced from 220
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(1)
                };
                Canvas.SetLeft(border, 0);
                Canvas.SetTop(border, 0);
                canvas.Children.Insert(0, border); // Add as first child so it's behind everything else
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
                    Width = 260, // Reduced from 360
                    Foreground = Brushes.Red
                };

                Canvas.SetLeft(errorTextBlock, 10);
                Canvas.SetTop(errorTextBlock, 70); // Adjusted position
                canvas.Children.Add(errorTextBlock);
            }

            return canvas;
        }
        private UIElement CreateBarcodeLabelVisual(MainStockDTO item, double width, double height)
        {
            // Create a container for the label content
            var canvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = Brushes.White
            };

            // Position the barcode image - use most of the available space
            double barcodeWidth = width * 0.9;
            double barcodeHeight = height * 0.5;

            try
            {
                // Check if item is null
                if (item == null)
                {
                    throw new ArgumentNullException("item", "Item cannot be null");
                }

                // Load barcode image with null check
                BitmapImage bitmapSource = null;
                if (item.BarcodeImage != null)
                {
                    bitmapSource = LoadBarcodeImage(item.BarcodeImage);
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
                    Canvas.SetTop(placeholder, height * 0.15);
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

                    // For crisp barcode lines
                    TextOptions.SetTextRenderingMode(barcodeImage, TextRenderingMode.ClearType);
                    TextOptions.SetTextFormattingMode(barcodeImage, TextFormattingMode.Display);

                    Canvas.SetLeft(barcodeImage, (width - barcodeWidth) / 2);
                    Canvas.SetTop(barcodeImage, height * 0.15);
                    canvas.Children.Add(barcodeImage);
                }

                // Add item name (with null check)
                var nameText = item.Name ?? "Unknown Item";
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

                // Position product name at top
                Canvas.SetLeft(nameTextBlock, (width - nameTextBlock.Width) / 2);
                Canvas.SetTop(nameTextBlock, height * 0.02);
                canvas.Children.Add(nameTextBlock);

                // Add barcode text (with null check)
                var barcodeText = item.Barcode ?? "No Barcode";
                var barcodeTextBlock = new TextBlock
                {
                    Text = barcodeText,
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 9,
                    TextAlignment = TextAlignment.Center,
                    Width = width * 0.9
                };

                // Position barcode text below where the barcode image would be
                double barcodeImageBottom = height * 0.15 + barcodeHeight;
                Canvas.SetLeft(barcodeTextBlock, (width - barcodeTextBlock.Width) / 2);
                Canvas.SetTop(barcodeTextBlock, barcodeImageBottom + 5);
                canvas.Children.Add(barcodeTextBlock);

                // Add price if needed
                if (item.SalePrice > 0)
                {
                    var priceTextBlock = new TextBlock
                    {
                        Text = $"${item.SalePrice:N2}",
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        Width = width * 0.9
                    };

                    // Position price at bottom
                    Canvas.SetLeft(priceTextBlock, (width - priceTextBlock.Width) / 2);
                    Canvas.SetTop(priceTextBlock, height * 0.8);
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
                Canvas.SetTop(errorTextBlock, height * 0.8);
                canvas.Children.Add(errorTextBlock);
            }

            return canvas;
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

                foreach (var item in Items.ToList())
                {
                    if (!string.IsNullOrWhiteSpace(item.Barcode) && item.BarcodeImage == null)
                    {
                        try
                        {
                            var barcodeData = _barcodeService.GenerateBarcode(item.Barcode);
                            if (barcodeData != null)
                            {
                                item.BarcodeImage = barcodeData;

                                var itemCopy = new MainStockDTO
                                {
                                    MainStockId = item.MainStockId,
                                    Name = item.Name,
                                    Barcode = item.Barcode,
                                    CategoryId = item.CategoryId,
                                    CategoryName = item.CategoryName,
                                    SupplierId = item.SupplierId,
                                    SupplierName = item.SupplierName,
                                    Description = item.Description,
                                    PurchasePrice = item.PurchasePrice,
                                    SalePrice = item.SalePrice,
                                    CurrentStock = item.CurrentStock,
                                    MinimumStock = item.MinimumStock,
                                    BarcodeImage = item.BarcodeImage,
                                    Speed = item.Speed,
                                    IsActive = item.IsActive,
                                    ImagePath = item.ImagePath,
                                    CreatedAt = item.CreatedAt,
                                    UpdatedAt = DateTime.Now
                                };

                                await _mainStockService.UpdateAsync(itemCopy);
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
                            Debug.WriteLine($"Error generating barcode for item {item.Name}: {ex.Message}");
                            // Continue with next item
                        }
                    }
                }

                StatusMessage = $"Successfully generated {generatedCount} barcode images.";
                await Task.Delay(2000);

                // Refresh items to ensure we have the latest data
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

                var viewModel = new BulkMainStockViewModel(
                    _mainStockService,
                    _categoryService,
                    _supplierService,
                    _barcodeService,
                    _supplierInvoiceService,  // This was already there
                    _imagePathService,        // Add this parameter
                    _productService,          // Add this parameter
                    _eventAggregator);

                // Get the current owner window
                var ownerWindow = GetOwnerWindow();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var dialog = new BulkMainStockDialog
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
                        ShowTemporaryErrorMessage($"Error showing bulk dialog: {ex.Message}");
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
                if (SelectedItem != null)
                {
                    SelectedItem.PropertyChanged -= SelectedItem_PropertyChanged;
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