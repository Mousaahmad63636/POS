using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.Services;

namespace QuickTechSystems.WPF.ViewModels
{
    public class ProductViewModel : ViewModelBase
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IBarcodeService _barcodeService;
        private readonly ISupplierService _supplierService;
        private readonly IGlobalOverlayService _overlayService;
        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private ProductDTO? _selectedProduct;
        private bool _isEditing;
        private bool _isDetailPanelVisible;
        private BitmapImage? _barcodeImage;
        private string _searchText = string.Empty;
        private bool _isAddMode;
        private Action<EntityChangedEvent<ProductDTO>> _productChangedHandler;
        private readonly Action<EntityChangedEvent<SupplierDTO>> _supplierChangedHandler;
        private readonly Action<EntityChangedEvent<CategoryDTO>> _categoryChangedHandler;

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

        public ProductDTO? SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                SetProperty(ref _selectedProduct, value);
                IsEditing = value != null;
                if (value?.BarcodeImage != null)
                {
                    LoadBarcodeImage(value.BarcodeImage);
                }
                else
                {
                    BarcodeImage = null;
                }
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public bool IsDetailPanelVisible
        {
            get => _isDetailPanelVisible;
            set => SetProperty(ref _isDetailPanelVisible, value);
        }

        public bool IsAddMode
        {
            get => _isAddMode;
            set => SetProperty(ref _isAddMode, value);
        }

        public BitmapImage? BarcodeImage
        {
            get => _barcodeImage;
            set => SetProperty(ref _barcodeImage, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                FilterProducts();
            }
        }

        public ICommand BulkAddCommand { get; }
        public ICommand LoadCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand GenerateBarcodeCommand { get; }
        public ICommand GenerateAutomaticBarcodeCommand { get; }

        public ProductViewModel(
            IProductService productService,
            ICategoryService categoryService,
            IBarcodeService barcodeService,
            ISupplierService supplierService,
            IEventAggregator eventAggregator,
             QuickTechSystems.WPF.Services.IGlobalOverlayService overlayService) : base(eventAggregator)
        {
            Debug.WriteLine("Initializing ProductViewModel");
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _barcodeService = barcodeService ?? throw new ArgumentNullException(nameof(barcodeService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));
            _overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));

            _products = new ObservableCollection<ProductDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();
            _productChangedHandler = HandleProductChanged;
            _categoryChangedHandler = HandleCategoryChanged;
            _supplierChangedHandler = HandleSupplierChanged;
            SubscribeToEvents();

            LoadCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            AddCommand = new RelayCommand(_ => AddNew());
            EditCommand = new RelayCommand(_ => EditProduct(), _ => SelectedProduct != null);
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
            DeleteCommand = new AsyncRelayCommand(async _ => await DeleteAsync());
            CancelCommand = new RelayCommand(_ => CancelEdit());
            GenerateBarcodeCommand = new RelayCommand(_ => GenerateBarcode());
            GenerateAutomaticBarcodeCommand = new RelayCommand(_ => GenerateAutomaticBarcode());
            BulkAddCommand = new AsyncRelayCommand(async _ => await ShowBulkAddDialog());

            _ = LoadDataAsync();
            Debug.WriteLine("ProductViewModel initialized");
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

        private async Task ShowBulkAddDialog()
        {
            try
            {
                // Create the view model
                var viewModel = new BulkProductViewModel(
                    _productService,
                    _categoryService,
                    _supplierService,
                    _barcodeService,
                    _eventAggregator);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    // Create and show the dialog on the UI thread
                    var dialog = new BulkProductDialog
                    {
                        DataContext = viewModel
                    };

                    // ShowDialog will handle setting the owner
                    var result = dialog.ShowDialog();

                    if (result == true)
                    {
                        await LoadDataAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error in bulk add: {ex.Message}");
            }
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
                            if (!Suppliers.Any(s => s.SupplierId == evt.Entity.SupplierId))
                            {
                                Suppliers.Add(evt.Entity);
                                Debug.WriteLine($"Added new supplier {evt.Entity.Name}");
                            }
                            break;
                        case "Update":
                            var existingIndex = Suppliers.ToList().FindIndex(s => s.SupplierId == evt.Entity.SupplierId);
                            if (existingIndex != -1)
                            {
                                Suppliers[existingIndex] = evt.Entity;
                                Debug.WriteLine($"Updated supplier {evt.Entity.Name}");
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
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    Debug.WriteLine("ProductViewModel: Reloading categories");
                    var categories = await _categoryService.GetAllAsync();
                    Categories = new ObservableCollection<CategoryDTO>(categories);
                    Debug.WriteLine($"ProductViewModel: Reloaded {Categories.Count} categories");
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
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Product refresh error: {ex.Message}");
            }
        }

        protected override async Task LoadDataAsync()
        {
            try
            {
                var products = await _productService.GetAllAsync();
                var categories = await _categoryService.GetAllAsync();
                var suppliers = await _supplierService.GetAllAsync();

                Products = new ObservableCollection<ProductDTO>(products);
                Categories = new ObservableCollection<CategoryDTO>(categories);
                Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddNew()
        {
            Console.WriteLine("AddNew method called");
            SelectedProduct = new ProductDTO
            {
                IsActive = true
            };
            BarcodeImage = null;
            IsAddMode = true;

            Console.WriteLine("Calling ShowProductEditor");
            _overlayService.ShowProductEditor(this);
            Console.WriteLine("ShowProductEditor called");
        }

        private void EditProduct()
        {
            if (SelectedProduct == null) return;
            IsAddMode = false;

            // Show the global overlay instead of the local one
            _overlayService.ShowProductEditor(this);
        }

        private async Task SaveAsync()
        {
            try
            {
                Debug.WriteLine("Starting save operation");
                if (SelectedProduct == null) return;

                if (string.IsNullOrWhiteSpace(SelectedProduct.Name))
                {
                    MessageBox.Show("Product name is required.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (SelectedProduct.CategoryId == 0)
                {
                    MessageBox.Show("Please select a category.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (SelectedProduct.ProductId == 0)
                {
                    var result = await _productService.CreateAsync(SelectedProduct);
                    Products.Add(result);
                }
                else
                {
                    await _productService.UpdateAsync(SelectedProduct);
                    var index = Products.IndexOf(Products.First(p => p.ProductId == SelectedProduct.ProductId));
                    Products[index] = SelectedProduct;
                }

                MessageBox.Show("Product saved successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                await LoadDataAsync();

                // Hide the global overlay
                _overlayService.HideProductEditor();

                IsAddMode = false;
                Debug.WriteLine("Save completed, data reloaded");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Save error: {ex.Message}");
                MessageBox.Show($"Error saving product: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelEdit()
        {
            // Hide the global overlay
            _overlayService.HideProductEditor();

            if (IsAddMode)
            {
                SelectedProduct = null;
            }
            IsAddMode = false;
        }

        private async Task DeleteAsync()
        {
            try
            {
                if (SelectedProduct == null) return;

                if (MessageBox.Show("Are you sure you want to delete this product?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        await _productService.DeleteAsync(SelectedProduct.ProductId);
                        await LoadDataAsync();
                        MessageBox.Show("Product deleted successfully.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        _overlayService.HideProductEditor();
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("cannot be physically deleted"))
                    {
                        // Handle the specific case where product was soft deleted
                        MessageBox.Show(ex.Message, "Product Marked as Inactive",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        await LoadDataAsync();
                        _overlayService.HideProductEditor();
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"Error deleting product: {ex.Message}";
                Debug.WriteLine($"{errorMessage}\nStack trace: {ex.StackTrace}");

                // Handle the case where deletion fails due to constraints
                if (ex.InnerException != null &&
                    (ex.InnerException.Message.Contains("FOREIGN KEY constraint") ||
                     ex.InnerException.Message.Contains("The DELETE statement conflicted with the REFERENCE constraint")))
                {
                    if (MessageBox.Show(
                        "This product cannot be deleted because it's used in transactions or inventory records. " +
                        "Would you like to mark it as inactive instead?",
                        "Cannot Delete Product",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        try
                        {
                            await _productService.SoftDeleteAsync(SelectedProduct.ProductId);
                            await LoadDataAsync();
                            MessageBox.Show("Product has been marked as inactive.", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            _overlayService.HideProductEditor();
                        }
                        catch (Exception softDeleteEx)
                        {
                            MessageBox.Show($"Error marking product as inactive: {softDeleteEx.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                else
                {
                    MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void FilterProducts()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                _ = LoadDataAsync();
                return;
            }

            var filteredProducts = Products.Where(p =>
                p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Barcode.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.SupplierName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

            Products = new ObservableCollection<ProductDTO>(filteredProducts);
        }

        private void GenerateBarcode()
        {
            if (SelectedProduct == null || string.IsNullOrWhiteSpace(SelectedProduct.Barcode))
            {
                MessageBox.Show("Please enter a barcode value first.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var barcodeData = _barcodeService.GenerateBarcode(SelectedProduct.Barcode);
                LoadBarcodeImage(barcodeData);
                SelectedProduct.BarcodeImage = barcodeData;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating barcode: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateAutomaticBarcode()
        {
            if (SelectedProduct == null)
            {
                MessageBox.Show("Please select a product first.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                var categoryPrefix = SelectedProduct.CategoryId.ToString().PadLeft(3, '0');
                var barcode = $"{categoryPrefix}{timestamp}";

                SelectedProduct.Barcode = barcode;
                var barcodeData = _barcodeService.GenerateBarcode(barcode);
                LoadBarcodeImage(barcodeData);
                SelectedProduct.BarcodeImage = barcodeData;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating automatic barcode: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadBarcodeImage(byte[] imageData)
        {
            var image = new BitmapImage();
            using (var ms = new MemoryStream(imageData))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
            }
            BarcodeImage = image;
        }
    }
}