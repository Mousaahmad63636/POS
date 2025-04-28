// Path: QuickTechSystems.WPF.ViewModels/BulkMainStockViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.Views;
using Microsoft.Win32;
using System.IO;
using System.Text;
using QuickTechSystems.Application.Services;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using System.Printing;
using System.Windows.Markup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace QuickTechSystems.WPF.ViewModels
{
    public class BulkMainStockViewModel : ViewModelBase
    {
        private readonly IMainStockService _mainStockService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private readonly IBarcodeService _barcodeService;
        private readonly ISupplierInvoiceService _supplierInvoiceService;
        private readonly IImagePathService _imagePathService;
        private readonly IProductService _productService;
        private bool _isSaving;
        private int _totalRows;
        private int _currentRow;
        private string _statusMessage;
        private ObservableCollection<MainStockDTO> _items;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private ObservableCollection<SupplierInvoiceDTO> _supplierInvoices;
        private string _csvContent;

        // New bulk selection properties
        private CategoryDTO _selectedBulkCategory;
        private SupplierDTO _selectedBulkSupplier;
        private SupplierInvoiceDTO _selectedBulkInvoice;
        private bool _showBulkSelectionPanel = true;
        private bool _generateBarcodesForNewItems = true;
        private int _labelsPerItem = 1;

        public bool IsSaving
        {
            get => _isSaving;
            set => SetProperty(ref _isSaving, value);
        }

        public int TotalRows
        {
            get => _totalRows;
            set => SetProperty(ref _totalRows, value);
        }

        public int CurrentRow
        {
            get => _currentRow;
            set => SetProperty(ref _currentRow, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<MainStockDTO> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
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

        public ObservableCollection<SupplierInvoiceDTO> SupplierInvoices
        {
            get => _supplierInvoices;
            set => SetProperty(ref _supplierInvoices, value);
        }

        public string CsvContent
        {
            get => _csvContent;
            set => SetProperty(ref _csvContent, value);
        }

        // New bulk selection properties
        public CategoryDTO SelectedBulkCategory
        {
            get => _selectedBulkCategory;
            set => SetProperty(ref _selectedBulkCategory, value);
        }

        public SupplierDTO SelectedBulkSupplier
        {
            get => _selectedBulkSupplier;
            set => SetProperty(ref _selectedBulkSupplier, value);
        }

        public SupplierInvoiceDTO SelectedBulkInvoice
        {
            get => _selectedBulkInvoice;
            set => SetProperty(ref _selectedBulkInvoice, value);
        }

        public bool ShowBulkSelectionPanel
        {
            get => _showBulkSelectionPanel;
            set => SetProperty(ref _showBulkSelectionPanel, value);
        }

        public bool GenerateBarcodesForNewItems
        {
            get => _generateBarcodesForNewItems;
            set => SetProperty(ref _generateBarcodesForNewItems, value);
        }

        public int LabelsPerItem
        {
            get => _labelsPerItem;
            set => SetProperty(ref _labelsPerItem, Math.Max(1, value));
        }

        // Existing commands
        public ICommand LoadDataCommand { get; }
        public ICommand ParseCsvCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand AddRowCommand { get; }
        public ICommand RemoveRowCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand DownloadTemplateCommand { get; }
        public ICommand GenerateAllBarcodesCommand { get; }

        // New commands
        public ICommand ApplyBulkCategoryCommand { get; }
        public ICommand ApplyBulkSupplierCommand { get; }
        public ICommand ApplyBulkInvoiceCommand { get; }
        public ICommand AddNewCategoryCommand { get; }
        public ICommand AddNewSupplierCommand { get; }
        public ICommand AddNewInvoiceCommand { get; }
        public ICommand UploadItemImageCommand { get; }
        public ICommand ClearItemImageCommand { get; }
        public ICommand PrintAllBarcodesCommand { get; }
        public ICommand ToggleBulkPanelCommand { get; }

        public BulkMainStockViewModel(
            IMainStockService mainStockService,
            ICategoryService categoryService,
            ISupplierService supplierService,
            IBarcodeService barcodeService,
            ISupplierInvoiceService supplierInvoiceService,
            IImagePathService imagePathService,
            IProductService productService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _mainStockService = mainStockService;
            _categoryService = categoryService;
            _supplierService = supplierService;
            _barcodeService = barcodeService;
            _supplierInvoiceService = supplierInvoiceService;
            _imagePathService = imagePathService;

            _items = new ObservableCollection<MainStockDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();
            _supplierInvoices = new ObservableCollection<SupplierInvoiceDTO>();
            _productService = productService;
            // Initialize existing commands
            LoadDataCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            ParseCsvCommand = new AsyncRelayCommand(async _ => await ParseCsvAsync());
            SaveAllCommand = new AsyncRelayCommand(async _ => await SaveAllAsync());
            AddRowCommand = new RelayCommand(_ => AddNewRow());
            RemoveRowCommand = new RelayCommand<MainStockDTO>(RemoveRow);
            ClearAllCommand = new RelayCommand(_ => ClearAll());
            DownloadTemplateCommand = new RelayCommand(_ => DownloadTemplate());
            GenerateAllBarcodesCommand = new RelayCommand(_ => GenerateAllBarcodes());

            // Initialize new commands
            ApplyBulkCategoryCommand = new RelayCommand(_ => ApplyBulkCategory());
            ApplyBulkSupplierCommand = new RelayCommand(_ => ApplyBulkSupplier());
            ApplyBulkInvoiceCommand = new RelayCommand(_ => ApplyBulkInvoice());
            AddNewCategoryCommand = new AsyncRelayCommand(async _ => await AddNewCategoryAsync());
            AddNewSupplierCommand = new AsyncRelayCommand(async _ => await AddNewSupplierAsync());
            AddNewInvoiceCommand = new AsyncRelayCommand(async _ => await AddNewInvoiceAsync());
            UploadItemImageCommand = new RelayCommand<MainStockDTO>(UploadItemImage);
            ClearItemImageCommand = new RelayCommand<MainStockDTO>(ClearItemImage);
            PrintAllBarcodesCommand = new AsyncRelayCommand(async _ => await PrintAllBarcodesAsync());
            ToggleBulkPanelCommand = new RelayCommand(_ => ShowBulkSelectionPanel = !ShowBulkSelectionPanel);

            // Initialize with default row
            AddNewRow();
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Loading data...";

                // Load categories
                var categories = await _categoryService.GetActiveAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Categories = new ObservableCollection<CategoryDTO>(categories);
                });

                // Load suppliers
                var suppliers = await _supplierService.GetActiveAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                });

                // Load draft supplier invoices
                var invoices = await _supplierInvoiceService.GetByStatusAsync("Draft");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>(invoices);
                });

                StatusMessage = "Data loaded successfully.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading data: {ex.Message}";
                Debug.WriteLine($"Error in BulkMainStockViewModel.LoadDataAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsSaving = false;
            }
        }

        private void AddNewRow()
        {
            var newItem = new MainStockDTO
            {
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            // Apply bulk selection if available
            if (SelectedBulkCategory != null)
            {
                newItem.CategoryId = SelectedBulkCategory.CategoryId;
                newItem.CategoryName = SelectedBulkCategory.Name;
            }

            if (SelectedBulkSupplier != null)
            {
                newItem.SupplierId = SelectedBulkSupplier.SupplierId;
                newItem.SupplierName = SelectedBulkSupplier.Name;
            }

            Items.Add(newItem);
        }

        private void RemoveRow(MainStockDTO item)
        {
            if (item != null)
            {
                Items.Remove(item);
            }
        }

        private void ClearAll()
        {
            var result = MessageBox.Show("Are you sure you want to clear all items?",
                "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Items.Clear();
                AddNewRow(); // Add one empty row
            }
        }

        private void ApplyBulkCategory()
        {
            if (SelectedBulkCategory == null) return;

            foreach (var item in Items)
            {
                item.CategoryId = SelectedBulkCategory.CategoryId;
                item.CategoryName = SelectedBulkCategory.Name;
            }

            StatusMessage = $"Applied category '{SelectedBulkCategory.Name}' to all items.";
        }

        private void ApplyBulkSupplier()
        {
            if (SelectedBulkSupplier == null) return;

            foreach (var item in Items)
            {
                item.SupplierId = SelectedBulkSupplier.SupplierId;
                item.SupplierName = SelectedBulkSupplier.Name;
            }

            StatusMessage = $"Applied supplier '{SelectedBulkSupplier.Name}' to all items.";
        }
        private void ApplyBulkInvoice()
        {
            if (SelectedBulkInvoice == null) return;

            Debug.WriteLine($"Applying invoice {SelectedBulkInvoice.InvoiceNumber} (ID: {SelectedBulkInvoice.SupplierInvoiceId}) to {Items.Count} items");

            foreach (var item in Items)
            {
                item.SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId;
            }

            StatusMessage = $"All items will be associated with invoice '{SelectedBulkInvoice.InvoiceNumber}' (ID: {SelectedBulkInvoice.SupplierInvoiceId}).";

            // Display confirmation to make it very clear
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"All items will be associated with invoice '{SelectedBulkInvoice.InvoiceNumber}'.\nPlease save the items to complete this association.",
                    "Invoice Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }
        private async Task AddNewCategoryAsync()
        {
            try
            {
                var dialog = new QuickCategoryDialogWindow
                {
                    Owner = GetOwnerWindow()
                };

                var result = dialog.ShowDialog();
                if (result == true && dialog.NewCategory != null)
                {
                    var newCategory = await _categoryService.CreateAsync(dialog.NewCategory);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Categories.Add(newCategory);
                        SelectedBulkCategory = newCategory;
                    });

                    StatusMessage = $"Category '{newCategory.Name}' added successfully.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding category: {ex.Message}";
                Debug.WriteLine($"Error in AddNewCategoryAsync: {ex}");
            }
        }

        private async Task AddNewSupplierAsync()
        {
            try
            {
                var dialog = new QuickSupplierDialogWindow
                {
                    Owner = GetOwnerWindow()
                };

                var result = dialog.ShowDialog();
                if (result == true && dialog.NewSupplier != null)
                {
                    var newSupplier = await _supplierService.CreateAsync(dialog.NewSupplier);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Suppliers.Add(newSupplier);
                        SelectedBulkSupplier = newSupplier;
                    });

                    StatusMessage = $"Supplier '{newSupplier.Name}' added successfully.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding supplier: {ex.Message}";
                Debug.WriteLine($"Error in AddNewSupplierAsync: {ex}");
            }
        }

        private async Task AddNewInvoiceAsync()
        {
            try
            {
                // Show the quick supplier invoice dialog
                var dialog = new QuickSupplierInvoiceDialog
                {
                    Owner = GetOwnerWindow()
                };

                var result = dialog.ShowDialog();
                if (result == true && dialog.CreatedInvoice != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        SupplierInvoices.Add(dialog.CreatedInvoice);
                        SelectedBulkInvoice = dialog.CreatedInvoice;
                    });

                    StatusMessage = $"Invoice '{dialog.CreatedInvoice.InvoiceNumber}' created successfully.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating invoice: {ex.Message}";
                Debug.WriteLine($"Error in AddNewInvoiceAsync: {ex}");
            }
        }

        private void UploadItemImage(MainStockDTO item)
        {
            if (item == null) return;

            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                    Title = "Select an image for the item"
                };

                if (dialog.ShowDialog() == true)
                {
                    string imagePath = _imagePathService.SaveProductImage(dialog.FileName);
                    item.ImagePath = imagePath;
                    StatusMessage = $"Image uploaded for item '{item.Name}'.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error uploading image: {ex.Message}";
                Debug.WriteLine($"Error in UploadItemImage: {ex}");
            }
        }

        private void ClearItemImage(MainStockDTO item)
        {
            if (item == null || string.IsNullOrEmpty(item.ImagePath)) return;

            try
            {
                _imagePathService.DeleteProductImage(item.ImagePath);
                item.ImagePath = null;
                StatusMessage = $"Image cleared for item '{item.Name}'.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error clearing image: {ex.Message}";
                Debug.WriteLine($"Error in ClearItemImage: {ex}");
            }
        }

        private void GenerateAllBarcodes()
        {
            try
            {
                int generatedCount = 0;

                foreach (var item in Items)
                {
                    // Skip items that already have barcodes
                    if (!string.IsNullOrWhiteSpace(item.Barcode))
                        continue;

                    // Generate a unique barcode
                    var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
                    var random = new Random();
                    var randomDigits = random.Next(1000, 9999).ToString();
                    var categoryPrefix = item.CategoryId > 0 ? item.CategoryId.ToString().PadLeft(3, '0') : "000";

                    item.Barcode = $"{categoryPrefix}{timestamp}{randomDigits}";
                    generatedCount++;

                    // Generate barcode image
                    try
                    {
                        item.BarcodeImage = _barcodeService.GenerateBarcode(item.Barcode);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                        // Continue despite error
                    }
                }

                StatusMessage = $"Generated {generatedCount} barcodes for items without barcodes.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error generating barcodes: {ex.Message}";
                Debug.WriteLine($"Error in GenerateAllBarcodes: {ex}");
            }
        }

        private async Task PrintAllBarcodesAsync()
        {
            try
            {
                StatusMessage = "Preparing barcodes for printing...";
                IsSaving = true;

                var selectedItems = Items.ToList();
                if (!selectedItems.Any())
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("No items available for printing.",
                            "No Items", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    return;
                }

                if (LabelsPerItem < 1)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Number of labels must be at least 1.",
                            "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    return;
                }

                // Generate missing barcodes first
                int generatedCount = 0;
                foreach (var item in selectedItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Barcode))
                    {
                        var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
                        var random = new Random();
                        var randomDigits = random.Next(1000, 9999).ToString();
                        var categoryPrefix = item.CategoryId.ToString().PadLeft(3, '0');
                        item.Barcode = $"{categoryPrefix}{timestamp}{randomDigits}";
                        generatedCount++;
                        StatusMessage = $"Generated {generatedCount} barcodes...";
                    }

                    if (item.BarcodeImage == null)
                    {
                        // Using higher resolution barcode generation (600x200)
                        item.BarcodeImage = _barcodeService.GenerateBarcode(item.Barcode, 600, 200);
                    }
                }

                bool printerCancelled = false;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var printDialog = new PrintDialog();
                        if (printDialog.ShowDialog() != true)
                        {
                            printerCancelled = true;
                            return;
                        }

                        var batchWindow = new BatchBarcodePrintWindow(selectedItems, LabelsPerItem, printDialog);
                        batchWindow.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error preparing print window: {ex.Message}",
                            "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });

                if (printerCancelled)
                {
                    StatusMessage = "Printing cancelled by user.";
                    await Task.Delay(1000);
                }
                else
                {
                    StatusMessage = "Barcodes printed successfully.";
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error printing barcodes: {ex.Message}";
                Debug.WriteLine($"Error printing barcodes: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error printing barcodes: {ex.Message}",
                        "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
            }
        }
        private FixedDocument CreateBarcodeDocument(List<MainStockDTO> items, int labelsPerItem, PrintDialog printDialog)
        {
            var document = new FixedDocument();
            var pageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);

            // Standard thermal label dimensions
            double deviceIndependentFactor = 96.0;

            // Define exact label size in device-independent pixels
            var labelWidth = 2 * deviceIndependentFactor;    // 2 inches
            var labelHeight = 1 * deviceIndependentFactor;   // 1 inch

            // Use minimal margins for thermal labels
            var margin = new Thickness(0.1 * deviceIndependentFactor);

            // Calculate how many labels can fit on the page
            var labelsPerRow = Math.Max(1, (int)Math.Floor((pageSize.Width - margin.Left - margin.Right) / labelWidth));
            var labelsPerColumn = Math.Max(1, (int)Math.Floor((pageSize.Height - margin.Top - margin.Bottom) / labelHeight));
            var labelsPerPage = labelsPerRow * labelsPerColumn;

            Debug.WriteLine($"Page can fit {labelsPerRow}x{labelsPerColumn} = {labelsPerPage} labels");

            var currentPage = CreateNewPage(pageSize, margin);
            var currentPanel = (WrapPanel)((FixedPage)currentPage.Child).Children[0];
            var labelCount = 0;

            foreach (var item in items)
            {
                for (int i = 0; i < labelsPerItem; i++)
                {
                    if (labelCount >= labelsPerPage)
                    {
                        document.Pages.Add(currentPage);
                        currentPage = CreateNewPage(pageSize, margin);
                        currentPanel = (WrapPanel)((FixedPage)currentPage.Child).Children[0];
                        labelCount = 0;
                    }

                    var labelVisual = CreateBarcodeLabelVisual(item, labelWidth, labelHeight);
                    currentPanel.Children.Add(labelVisual);
                    labelCount++;
                }
            }

            if (labelCount > 0)
            {
                document.Pages.Add(currentPage);
            }

            return document;
        }

        private PageContent CreateNewPage(Size pageSize, Thickness margin)
        {
            var pageContent = new PageContent();
            var fixedPage = new FixedPage();
            fixedPage.Width = pageSize.Width;
            fixedPage.Height = pageSize.Height;

            // Create a WrapPanel to hold the labels
            var wrapPanel = new WrapPanel
            {
                Width = pageSize.Width - margin.Left - margin.Right,
                Height = pageSize.Height - margin.Top - margin.Bottom,
                Margin = margin,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            fixedPage.Children.Add(wrapPanel);
            ((IAddChild)pageContent).AddChild(fixedPage);

            return pageContent;
        }

        private UIElement CreateBarcodeLabelVisual(MainStockDTO item, double width, double height)
        {
            // Create a container for the label content
            var canvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = Brushes.White,
                Margin = new Thickness(2) // Small margin between labels
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

            // Add a border around the entire label for visual separation
            var border = new Border
            {
                Width = width,
                Height = height,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1)
            };
            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, 0);
            canvas.Children.Insert(0, border); // Add as first child so it's behind everything else

            return canvas;
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
        private void DownloadTemplate()
        {
            try
            {
                // Create a more comprehensive CSV template
                string template = "Name,Barcode,CategoryId,SupplierId,PurchasePrice,SalePrice,CurrentStock,MinimumStock,Description,Speed\n" +
                                  "Sample Product,ABC123,1,1,10.00,15.00,100,10,\"Product description\",1.5\n" +
                                  "Another Product,,2,2,20.00,30.00,50,5,\"Another description\",2.0";

                // Open save dialog
                var dialog = new SaveFileDialog
                {
                    FileName = "MainStock_Template",
                    DefaultExt = ".csv",
                    Filter = "CSV Files (*.csv)|*.csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, template);
                    StatusMessage = "Template downloaded successfully.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error downloading template: {ex.Message}";
                Debug.WriteLine($"Error in DownloadTemplate: {ex}");
            }
        }

        private async Task ParseCsvAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(CsvContent))
                {
                    StatusMessage = "Please enter CSV content.";
                    return;
                }

                IsSaving = true;
                StatusMessage = "Parsing CSV...";

                // Parse the CSV content
                var lines = CsvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                // Check if there's at least one line (header)
                if (lines.Length < 2)
                {
                    StatusMessage = "CSV content is too short or invalid. It should have a header line and at least one data line.";
                    return;
                }

                // Parse header to get column indices
                var headerLine = lines[0];
                var headers = SplitCsvLine(headerLine);

                var nameIndex = Array.IndexOf(headers, "Name");
                var barcodeIndex = Array.IndexOf(headers, "Barcode");
                var categoryIdIndex = Array.IndexOf(headers, "CategoryId");
                var supplierIdIndex = Array.IndexOf(headers, "SupplierId");
                var purchasePriceIndex = Array.IndexOf(headers, "PurchasePrice");
                var salePriceIndex = Array.IndexOf(headers, "SalePrice");
                var currentStockIndex = Array.IndexOf(headers, "CurrentStock");
                var minimumStockIndex = Array.IndexOf(headers, "MinimumStock");
                var descriptionIndex = Array.IndexOf(headers, "Description");
                var speedIndex = Array.IndexOf(headers, "Speed");

                // Validate required columns
                if (nameIndex == -1 || categoryIdIndex == -1)
                {
                    StatusMessage = "Missing required columns in CSV (Name and CategoryId are required).";
                    return;
                }

                // Clear existing items and parse data lines
                Items.Clear();

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var values = SplitCsvLine(line);

                    if (values.Length <= Math.Max(nameIndex, categoryIdIndex))
                    {
                        StatusMessage = $"Line {i + 1} has too few columns.";
                        continue;
                    }

                    var item = new MainStockDTO
                    {
                        Name = nameIndex >= 0 && nameIndex < values.Length ? values[nameIndex] : "",
                        Barcode = barcodeIndex >= 0 && barcodeIndex < values.Length ? values[barcodeIndex] : "",
                        CategoryId = categoryIdIndex >= 0 && categoryIdIndex < values.Length && int.TryParse(values[categoryIdIndex], out int catId) ? catId : 0,
                        SupplierId = supplierIdIndex >= 0 && supplierIdIndex < values.Length && !string.IsNullOrEmpty(values[supplierIdIndex]) && int.TryParse(values[supplierIdIndex], out int suppId) ? suppId : null,
                        PurchasePrice = purchasePriceIndex >= 0 && purchasePriceIndex < values.Length && decimal.TryParse(values[purchasePriceIndex], out decimal pp) ? pp : 0,
                        SalePrice = salePriceIndex >= 0 && salePriceIndex < values.Length && decimal.TryParse(values[salePriceIndex], out decimal sp) ? sp : 0,
                        CurrentStock = currentStockIndex >= 0 && currentStockIndex < values.Length && decimal.TryParse(values[currentStockIndex], out decimal cs) ? cs : 0,
                        MinimumStock = minimumStockIndex >= 0 && minimumStockIndex < values.Length && int.TryParse(values[minimumStockIndex], out int ms) ? ms : 0,
                        Description = descriptionIndex >= 0 && descriptionIndex < values.Length ? values[descriptionIndex] : null,
                        Speed = speedIndex >= 0 && speedIndex < values.Length ? values[speedIndex] : null,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    };

                    // Look up category name
                    if (item.CategoryId > 0)
                    {
                        var category = Categories.FirstOrDefault(c => c.CategoryId == item.CategoryId);
                        if (category != null)
                        {
                            item.CategoryName = category.Name;
                        }
                    }

                    // Look up supplier name
                    if (item.SupplierId.HasValue && item.SupplierId.Value > 0)
                    {
                        var supplier = Suppliers.FirstOrDefault(s => s.SupplierId == item.SupplierId);
                        if (supplier != null)
                        {
                            item.SupplierName = supplier.Name;
                        }
                    }

                    // Set bulk invoice if selected
                    if (SelectedBulkInvoice != null)
                    {
                        item.SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId;
                    }

                    // Generate barcode image if auto-generate is enabled
                    if (GenerateBarcodesForNewItems && !string.IsNullOrWhiteSpace(item.Barcode))
                    {
                        try
                        {
                            item.BarcodeImage = _barcodeService.GenerateBarcode(item.Barcode);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error generating barcode image for {item.Name}: {ex.Message}");
                            // Continue despite error
                        }
                    }

                    Items.Add(item);
                }

                StatusMessage = $"Parsed {Items.Count} items from CSV.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error parsing CSV: {ex.Message}";
                Debug.WriteLine($"Error in ParseCsvAsync: {ex}");
            }
            finally
            {
                IsSaving = false;
            }
        }

        // Helper method to split CSV line handling quoted values
        private string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var currentValue = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentValue.ToString());
                    currentValue.Clear();
                }
                else
                {
                    currentValue.Append(c);
                }
            }

            // Add the last value
            result.Add(currentValue.ToString());
            return result.ToArray();
        }

        private async Task SaveAllAsync()
        {
            try
            {
                // Validate items
                var invalidItems = Items.Where(i =>
                    string.IsNullOrWhiteSpace(i.Name) ||
                    i.CategoryId <= 0 ||
                    i.SalePrice <= 0).ToList();

                if (invalidItems.Any())
                {
                    var missingFields = new List<string>();
                    foreach (var item in invalidItems)
                    {
                        var fieldsMessage = "Missing required fields: ";
                        var fields = new List<string>();

                        if (string.IsNullOrWhiteSpace(item.Name))
                            fields.Add("Name");

                        if (item.CategoryId <= 0)
                            fields.Add("Category");

                        if (item.SalePrice <= 0)
                            fields.Add("Sale Price");

                        fieldsMessage += string.Join(", ", fields);
                        missingFields.Add($"• {item.Name ?? "Unnamed item"}: {fieldsMessage}");
                    }

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Please fix the following issues before saving:\n\n{string.Join("\n", missingFields)}",
                            "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });

                    return;
                }

                // Generate barcodes for items that need them
                if (GenerateBarcodesForNewItems)
                {
                    foreach (var item in Items.Where(i => string.IsNullOrWhiteSpace(i.Barcode)))
                    {
                        var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
                        var random = new Random();
                        var randomDigits = random.Next(1000, 9999).ToString();
                        var categoryPrefix = item.CategoryId.ToString().PadLeft(3, '0');

                        item.Barcode = $"{categoryPrefix}{timestamp}{randomDigits}";

                        // Generate barcode image
                        try
                        {
                            item.BarcodeImage = _barcodeService.GenerateBarcode(item.Barcode);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                            // Continue despite error
                        }
                    }
                }

                IsSaving = true;
                StatusMessage = "Saving items...";

                // Set up progress reporting
                TotalRows = Items.Count;
                CurrentRow = 0;

                var progress = new Progress<string>(status =>
                {
                    StatusMessage = status;
                    CurrentRow++;
                });

                // Save items in bulk
                var savedItems = await _mainStockService.CreateBatchAsync(Items.ToList(), progress);

                // Associate items with the selected invoice if specified
                if (SelectedBulkInvoice != null && savedItems.Any())
                {
                    StatusMessage = "Checking invoice status...";

                    // First verify the invoice exists and is in draft status
                    var invoice = await _supplierInvoiceService.GetByIdAsync(SelectedBulkInvoice.SupplierInvoiceId);
                    if (invoice == null)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show($"Could not find invoice with ID {SelectedBulkInvoice.SupplierInvoiceId}",
                                "Invoice Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                    else if (invoice.Status != "Draft")
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show($"Cannot add products to invoice in '{invoice.Status}' status. Only Draft invoices can be modified.",
                                "Invalid Invoice Status", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                    }
                    else
                    {
                        StatusMessage = "Associating items with invoice...";
                        int successCount = 0;
                        int errorCount = 0;
                        string lastErrorMessage = string.Empty;

                        foreach (var item in savedItems)
                        {
                            try
                            {
                                // IMPORTANT: The MainStock items are now saved, use their IDs
                                Debug.WriteLine($"Processing MainStock item {item.Name} (ID: {item.MainStockId}) for invoice");

                                // Create a Product without setting MainStockId yet
                                var newProduct = new ProductDTO
                                {
                                    Name = item.Name,
                                    Barcode = item.Barcode,
                                    CategoryId = item.CategoryId,
                                    CategoryName = item.CategoryName,
                                    SupplierId = item.SupplierId,
                                    SupplierName = item.SupplierName,
                                    Description = item.Description,
                                    PurchasePrice = item.PurchasePrice,
                                    SalePrice = item.SalePrice,
                                    CurrentStock = 0, // Start with zero stock
                                    MinimumStock = item.MinimumStock,
                                    ImagePath = item.ImagePath,
                                    Speed = item.Speed,
                                    IsActive = item.IsActive
                                    // Do NOT set MainStockId here
                                };

                                // Save the Product first
                                var savedProduct = await _productService.CreateAsync(newProduct);
                                Debug.WriteLine($"Created new Product with ID: {savedProduct.ProductId}");

                                // Now create the invoice detail
                                var invoiceDetail = new SupplierInvoiceDetailDTO
                                {
                                    SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId,
                                    ProductId = savedProduct.ProductId,
                                    ProductName = item.Name,
                                    ProductBarcode = item.Barcode,
                                    Quantity = item.CurrentStock > 0 ? item.CurrentStock : 1, // Ensure quantity is never zero
                                    PurchasePrice = item.PurchasePrice,
                                    TotalPrice = item.PurchasePrice * (item.CurrentStock > 0 ? item.CurrentStock : 1)
                                };

                                Debug.WriteLine($"Adding product {savedProduct.ProductId} to invoice {SelectedBulkInvoice.SupplierInvoiceId}");
                                await _supplierInvoiceService.AddProductToInvoiceAsync(invoiceDetail);
                                Debug.WriteLine($"Successfully added product to invoice: {item.Name}");
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                lastErrorMessage = ex.Message;
                                Debug.WriteLine($"Error associating item with invoice: {ex.Message}");
                                if (ex.InnerException != null)
                                {
                                    Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                                }
                                // Continue with other items
                            }
                        }

                        // Show a message with the results
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (successCount > 0)
                            {
                                string message = $"Successfully added {successCount} products to invoice '{SelectedBulkInvoice.InvoiceNumber}'.";
                                if (errorCount > 0)
                                {
                                    message += $"\n\n{errorCount} items couldn't be added. Last error: {lastErrorMessage}";
                                }

                                MessageBox.Show(message, "Invoice Association",
                                    MessageBoxButton.OK, errorCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                            }
                            else if (errorCount > 0)
                            {
                                MessageBox.Show($"Failed to add any products to the invoice. Error: {lastErrorMessage}",
                                    "Invoice Association Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        });
                    }
                }

                StatusMessage = $"Successfully saved {savedItems.Count} items.";

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Successfully saved {savedItems.Count} items.",
                        "Bulk Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Close dialog with success result
                    DialogResult = true;
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving items: {ex.Message}";
                Debug.WriteLine($"Error in SaveAllAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error saving items: {ex.Message}",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsSaving = false;
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

        // Property for dialog result
        private bool? _dialogResult;
        public bool? DialogResult
        {
            get => _dialogResult;
            set => SetProperty(ref _dialogResult, value);
        }
    }
}