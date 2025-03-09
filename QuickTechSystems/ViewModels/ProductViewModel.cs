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
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;
using System.Threading;

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

        private ObservableCollection<ProductDTO> _products;
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

                    ProductImage = value.Image != null ? LoadImage(value.Image) : null;

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
                SetProperty(ref _searchText, value);
                FilterProducts();
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

        public ICommand BulkAddCommand { get; private set; }
        public ICommand LoadCommand { get; private set; }
        public ICommand AddCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand GenerateBarcodeCommand { get; private set; }
        public ICommand GenerateAutomaticBarcodeCommand { get; private set; }
        public ICommand UpdateStockCommand { get; private set; }
        public ICommand PrintBarcodeCommand { get; private set; }
        public ICommand UploadImageCommand { get; private set; }
        public ICommand ClearImageCommand { get; private set; }

        public ProductViewModel(
            IProductService productService,
            ICategoryService categoryService,
            IBarcodeService barcodeService,
            ISupplierService supplierService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            Debug.WriteLine("Initializing ProductViewModel");
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _barcodeService = barcodeService ?? throw new ArgumentNullException(nameof(barcodeService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));

            _products = new ObservableCollection<ProductDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();
            _validationErrors = new Dictionary<int, List<string>>();
            _productChangedHandler = HandleProductChanged;
            _categoryChangedHandler = HandleCategoryChanged;
            _supplierChangedHandler = HandleSupplierChanged;

            SubscribeToEvents();
            InitializeCommands();
            _ = LoadDataAsync();
            Debug.WriteLine("ProductViewModel initialized");
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
            IsProductPopupOpen = true;
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
                        var imageBytes = File.ReadAllBytes(openFileDialog.FileName);
                        SelectedProduct.Image = imageBytes;
                        ProductImage = LoadImage(imageBytes);
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
                SelectedProduct.Image = null;
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

                if (SelectedProduct.BarcodeImage == null)
                {
                    ShowTemporaryErrorMessage("Please generate a barcode first.");
                    return;
                }

                IsSaving = true;
                StatusMessage = "Preparing barcode for printing...";

                await Task.Run(() =>
                {
                    return System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var printDialog = new PrintDialog();
                        if (printDialog.ShowDialog() == true)
                        {
                            var document = CreateBarcodeDocument(SelectedProduct, LabelsPerProduct);
                            printDialog.PrintDocument(document.DocumentPaginator, "Product Barcode");
                        }
                    });
                });

                StatusMessage = "Barcode printed successfully.";
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error printing barcode: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
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
            var grid = new Grid
            {
                Width = labelSize.Width,
                Height = labelSize.Height,
                Margin = new Thickness(2)
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var barcodeImage = LoadBarcodeImage(product.BarcodeImage);
            var image = new Image
            {
                Source = barcodeImage,
                Stretch = Stretch.Uniform
            };
            Grid.SetRow(image, 0);

            var barcodeText = new TextBlock
            {
                Text = product.Barcode,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(barcodeText, 1);

            var nameText = new TextBlock
            {
                Text = product.Name,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(nameText, 2);

            grid.Children.Add(image);
            grid.Children.Add(barcodeText);
            grid.Children.Add(nameText);

            return grid;
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
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Product refresh error: {ex.Message}");
            }
        }

        protected override async Task LoadDataAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                Debug.WriteLine("LoadDataAsync skipped - already in progress");
                return;
            }

            try
            {
                IsSaving = true;
                StatusMessage = "Loading data...";

                var products = await _productService.GetAllAsync();
                // Change this line to get only active categories
                var categories = await _categoryService.GetActiveAsync();
                var suppliers = await _supplierService.GetActiveAsync();

                Products = new ObservableCollection<ProductDTO>(products);
                Categories = new ObservableCollection<CategoryDTO>(categories);
                Suppliers = new ObservableCollection<SupplierDTO>(suppliers);

                // Calculate values after loading products
                CalculateAggregatedValues();

                if (SelectedProduct != null)
                {
                    CalculateSelectedProductValues();
                }
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error loading data: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
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
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                _ = LoadDataAsync();
                return;
            }

            var filteredProducts = Products.Where(p =>
                p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Barcode.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.SupplierName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (p.Speed?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

            Products = new ObservableCollection<ProductDTO>(filteredProducts);

            // Recalculate after filtering
            CalculateAggregatedValues();
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
                if (SelectedProduct == null) return;

                IsSaving = true;
                StatusMessage = "Validating product...";

                // Store reference to product before any operations that might clear it
                var productToUpdate = SelectedProduct;

                if (!ValidateProduct(productToUpdate))
                {
                    return;
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
                    Image = productToUpdate.Image,
                    CreatedAt = productToUpdate.CreatedAt,
                    UpdatedAt = DateTime.Now
                };

                if (productToUpdate.ProductId == 0)
                {
                    var result = await _productService.CreateAsync(productCopy);

                    // Important: Use dispatcher for UI updates
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        Products.Add(result);
                        SelectedProduct = result;
                    });

                    // Use the result for updating UI, not SelectedProduct which could be null after async operation
                    productToUpdate = result;
                }
                else
                {
                    await _productService.UpdateAsync(productCopy);

                    // Update the existing product in the collection using the dispatcher
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        // Find and update the existing product in the collection
                        for (int i = 0; i < Products.Count; i++)
                        {
                            if (Products[i].ProductId == productCopy.ProductId)
                            {
                                Products[i] = productCopy;
                                Debug.WriteLine($"Directly updated product in collection: {productCopy.Name}");
                                break;
                            }
                        }
                    });
                }

                // Explicitly publish the update event - use stored reference, not SelectedProduct
                try
                {
                    Debug.WriteLine($"Publishing refresh event for product: {productToUpdate.Name}, IsActive: {productToUpdate.IsActive}");
                    _eventAggregator.Publish(new EntityChangedEvent<ProductDTO>("Update", productToUpdate));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error publishing product update event: {ex.Message}");
                    // Continue execution even if event publishing fails
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

                // Important: Don't reload full data which can overwrite our updates
                // Instead, just refresh the one product we updated
                await RefreshSpecificProduct(productToUpdate.ProductId);

                Debug.WriteLine("Save completed, product refreshed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Save error: {ex.Message}");
                ShowTemporaryErrorMessage($"Error saving product: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }

        // Add this new method to refresh a specific product without reloading everything
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

            if (product.PurchasePrice <= 0)
                errors.Add("Purchase price must be greater than zero");

            if (product.CurrentStock < 0)
                errors.Add("Current stock cannot be negative");

            if (product.MinimumStock < 0)
                errors.Add("Minimum stock cannot be negative");

            if (product.MinimumStock > product.CurrentStock)
                errors.Add("Minimum stock cannot exceed current stock");

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
                    return MessageBox.Show("Are you sure you want to delete this product?",
                        "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                });

                if (result == MessageBoxResult.Yes)
                {
                    IsSaving = true;
                    StatusMessage = "Deleting product...";

                    await _productService.DeleteAsync(SelectedProduct.ProductId);
                    CloseProductPopup();
                    await LoadDataAsync();

                    SelectedProduct = null;
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
                var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                var random = new Random();
                var randomDigits = random.Next(1000, 9999).ToString();
                var categoryPrefix = SelectedProduct.CategoryId.ToString().PadLeft(3, '0');

                SelectedProduct.Barcode = $"{categoryPrefix}{timestamp}{randomDigits}";
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

                _operationLock?.Dispose();
                UnsubscribeFromEvents();

                _isDisposed = true;
            }

            base.Dispose();
        }
    }
}