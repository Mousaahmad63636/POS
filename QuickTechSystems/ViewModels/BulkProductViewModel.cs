// QuickTechSystems/ViewModels/BulkProductViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.WPF.ViewModels
{
    public class BulkProductViewModel : ViewModelBase
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private readonly IBarcodeService _barcodeService; // Add this line
        private ObservableCollection<ProductDTO> _products;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private bool? _dialogResult;
        private string _selectedQuickFillOption;
        private string _quickFillValue;

        public bool? DialogResult
        {
            get => _dialogResult;
            set => SetProperty(ref _dialogResult, value);
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

        public string SelectedQuickFillOption
        {
            get => _selectedQuickFillOption;
            set => SetProperty(ref _selectedQuickFillOption, value);
        }

        public string QuickFillValue
        {
            get => _quickFillValue;
            set => SetProperty(ref _quickFillValue, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand AddFiveRowsCommand { get; }
        public ICommand AddTenRowsCommand { get; }
        public ICommand ClearEmptyRowsCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand ApplyQuickFillCommand { get; }
        public ICommand GenerateBarcodeCommand { get; }
        public ICommand ImportFromExcelCommand { get; }

        public BulkProductViewModel(
            IProductService productService,
            ICategoryService categoryService,
            ISupplierService supplierService,
            IBarcodeService barcodeService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _productService = productService;
            _categoryService = categoryService;
            _supplierService = supplierService;
            _selectedQuickFillOption = "Category";
            _barcodeService = barcodeService;
            _quickFillValue = string.Empty;

            _products = new ObservableCollection<ProductDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();

            SaveCommand = new AsyncRelayCommand(async _ => await SaveProductsAsync());
            AddFiveRowsCommand = new RelayCommand(_ => AddEmptyRows(5));
            AddTenRowsCommand = new RelayCommand(_ => AddEmptyRows(10));
            ClearEmptyRowsCommand = new RelayCommand(_ => ClearEmptyRows());
            ClearAllCommand = new RelayCommand(_ => ClearAllRows());
            ApplyQuickFillCommand = new RelayCommand(ApplyQuickFill);
            GenerateBarcodeCommand = new RelayCommand(GenerateBarcode);
            ImportFromExcelCommand = new AsyncRelayCommand(async _ => await ImportFromExcel());

            LoadInitialData();
            Products.Add(new ProductDTO { IsActive = true });
        }

        private async void LoadInitialData()
        {
            try
            {
                var categories = await _categoryService.GetAllAsync();
                var suppliers = await _supplierService.GetAllAsync();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Categories = new ObservableCollection<CategoryDTO>(categories);
                    Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                });
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading data: {ex.Message}");
            }
        }
        private void GenerateBarcode(object? parameter)
        {
            if (parameter is not ProductDTO product)
                return;

            try
            {
                var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                var categoryPrefix = product.CategoryId.ToString().PadLeft(3, '0');
                var barcode = $"{categoryPrefix}{timestamp}";

                product.Barcode = barcode;
                OnPropertyChanged(nameof(Products));

                // Optional: Show confirmation message
                MessageBox.Show($"Barcode generated: {barcode}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating barcode: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async Task LoadInitialDataAsync()
        {
            try
            {
                var categories = await _categoryService.GetAllAsync();
                var suppliers = await _supplierService.GetAllAsync();

                Categories = new ObservableCollection<CategoryDTO>(categories);
                Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading data: {ex.Message}");
            }
        }

        private void AddEmptyRows(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Products.Add(new ProductDTO { IsActive = true });
            }
            OnPropertyChanged(nameof(Products));
        }

        private void ClearEmptyRows()
        {
            var emptyRows = Products.Where(p =>
                string.IsNullOrWhiteSpace(p.Name) &&
                string.IsNullOrWhiteSpace(p.Barcode) &&
                p.CategoryId == 0).ToList();

            foreach (var row in emptyRows)
            {
                Products.Remove(row);
            }

            if (Products.Count == 0)
            {
                Products.Add(new ProductDTO { IsActive = true });
            }

            OnPropertyChanged(nameof(Products));
        }

        private void ClearAllRows()
        {
            if (MessageBox.Show("Are you sure you want to clear all rows?",
                "Confirm Clear All", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                Products.Clear();
                Products.Add(new ProductDTO { IsActive = true });
                OnPropertyChanged(nameof(Products));
            }
        }

        private void ApplyQuickFill(object? parameter)
        {
            var selectedProducts = Products.Where(p => p.IsSelected).ToList();
            if (!selectedProducts.Any())
            {
                MessageBox.Show("Please select at least one row to apply quick fill.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            switch (SelectedQuickFillOption)
            {
                case "Category":
                    var category = Categories.FirstOrDefault();
                    if (category != null)
                    {
                        foreach (var product in selectedProducts)
                        {
                            product.CategoryId = category.CategoryId;
                        }
                    }
                    break;

                case "Supplier":
                    var supplier = Suppliers.FirstOrDefault();
                    if (supplier != null)
                    {
                        foreach (var product in selectedProducts)
                        {
                            product.SupplierId = supplier.SupplierId;
                        }
                    }
                    break;

                case "Purchase Price":
                    if (decimal.TryParse(QuickFillValue, out decimal purchasePrice))
                    {
                        foreach (var product in selectedProducts)
                        {
                            product.PurchasePrice = purchasePrice;
                        }
                    }
                    break;

                case "Sale Price":
                    if (decimal.TryParse(QuickFillValue, out decimal salePrice))
                    {
                        foreach (var product in selectedProducts)
                        {
                            product.SalePrice = salePrice;
                        }
                    }
                    break;

                case "Stock Values":
                    if (int.TryParse(QuickFillValue, out int stockValue))
                    {
                        foreach (var product in selectedProducts)
                        {
                            product.CurrentStock = stockValue;
                            product.MinimumStock = stockValue / 2;
                        }
                    }
                    break;
            }

            OnPropertyChanged(nameof(Products));
        }

        private async Task ImportFromExcel()
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Excel Files|*.xlsx;*.xls|All files (*.*)|*.*",
                    Title = "Select Excel File"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    await ShowErrorMessageAsync("Excel import functionality will be implemented in a future update.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error importing from Excel: {ex.Message}");
            }
        }

        private async Task SaveProductsAsync()
        {
            try
            {
                var validProducts = Products.Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.CategoryId != 0).ToList();
                if (!validProducts.Any())
                {
                    MessageBox.Show("No valid products to save.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (var product in validProducts)
                {
                    await _productService.CreateAsync(product);
                }

                MessageBox.Show($"{validProducts.Count} products saved successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error saving products: {ex.Message}");
            }
        }
    }
}