// Path: QuickTechSystems.WPF.Views/ProductEditWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;

namespace QuickTechSystems.WPF.Views
{
    public partial class ProductEditWindow : Window, INotifyPropertyChanged
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private ProductDTO _originalProduct;
        private ProductDTO _editingProduct;
        private bool _isSaving;
        private string _validationMessage = string.Empty;
        private bool _hasValidationErrors;

        public ProductDTO EditingProduct
        {
            get => _editingProduct;
            set
            {
                _editingProduct = value;
                OnPropertyChanged();
                ValidateProduct();
            }
        }

        public ObservableCollection<CategoryDTO> Categories { get; private set; }
        public ObservableCollection<SupplierDTO> Suppliers { get; private set; }

        public bool IsSaving
        {
            get => _isSaving;
            set
            {
                _isSaving = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
            }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            set
            {
                _validationMessage = value;
                OnPropertyChanged();
            }
        }

        public bool HasValidationErrors
        {
            get => _hasValidationErrors;
            set
            {
                _hasValidationErrors = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
            }
        }

        public bool CanSave => !IsSaving && !HasValidationErrors;

        // Properties that expose the editing product's properties for binding
        public string Name
        {
            get => EditingProduct?.Name ?? string.Empty;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.Name = value;
                    OnPropertyChanged();
                    ValidateProduct();
                }
            }
        }

        public string Description
        {
            get => EditingProduct?.Description ?? string.Empty;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.Description = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Barcode
        {
            get => EditingProduct?.Barcode ?? string.Empty;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.Barcode = value;
                    OnPropertyChanged();
                    ValidateProduct();
                }
            }
        }

        public string BoxBarcode
        {
            get => EditingProduct?.BoxBarcode ?? string.Empty;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.BoxBarcode = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CategoryId
        {
            get => EditingProduct?.CategoryId ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.CategoryId = value;
                    CategoryDTO category = Categories?.FirstOrDefault(c => c.CategoryId == value);
                    if (category != null)
                    {
                        EditingProduct.CategoryName = category.Name;
                    }
                    OnPropertyChanged();
                    ValidateProduct();
                }
            }
        }

        public int? SupplierId
        {
            get => EditingProduct?.SupplierId;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.SupplierId = value;
                    SupplierDTO supplier = Suppliers?.FirstOrDefault(s => s.SupplierId == value);
                    EditingProduct.SupplierName = supplier?.Name ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public decimal PurchasePrice
        {
            get => EditingProduct?.PurchasePrice ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.PurchasePrice = value;
                    OnPropertyChanged();
                    ValidateProduct();
                }
            }
        }

        public decimal WholesalePrice
        {
            get => EditingProduct?.WholesalePrice ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.WholesalePrice = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal SalePrice
        {
            get => EditingProduct?.SalePrice ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.SalePrice = value;
                    OnPropertyChanged();
                    ValidateProduct();
                }
            }
        }

        public int CurrentStock
        {
            get => EditingProduct?.CurrentStock ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.CurrentStock = value;
                    OnPropertyChanged();
                }
            }
        }

        public int MinimumStock
        {
            get => EditingProduct?.MinimumStock ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.MinimumStock = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ItemsPerBox
        {
            get => EditingProduct?.ItemsPerBox ?? 1;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.ItemsPerBox = Math.Max(1, value);
                    OnPropertyChanged();
                }
            }
        }

        public int NumberOfBoxes
        {
            get => EditingProduct?.NumberOfBoxes ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.NumberOfBoxes = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal BoxPurchasePrice
        {
            get => EditingProduct?.BoxPurchasePrice ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.BoxPurchasePrice = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal BoxWholesalePrice
        {
            get => EditingProduct?.BoxWholesalePrice ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.BoxWholesalePrice = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal BoxSalePrice
        {
            get => EditingProduct?.BoxSalePrice ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.BoxSalePrice = value;
                    OnPropertyChanged();
                }
            }
        }

        public int MinimumBoxStock
        {
            get => EditingProduct?.MinimumBoxStock ?? 0;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.MinimumBoxStock = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Speed
        {
            get => EditingProduct?.Speed ?? string.Empty;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.Speed = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ImagePath
        {
            get => EditingProduct?.ImagePath ?? string.Empty;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.ImagePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsActive
        {
            get => EditingProduct?.IsActive ?? true;
            set
            {
                if (EditingProduct != null)
                {
                    EditingProduct.IsActive = value;
                    OnPropertyChanged();
                }
            }
        }

        public ProductEditWindow(
            ProductDTO productToEdit,
            IProductService productService,
            ICategoryService categoryService,
            ISupplierService supplierService)
        {
            InitializeComponent();

            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));

            _originalProduct = productToEdit ?? throw new ArgumentNullException(nameof(productToEdit));

            // Create a deep copy for editing
            _editingProduct = CreateProductCopy(_originalProduct);

            Categories = new ObservableCollection<CategoryDTO>();
            Suppliers = new ObservableCollection<SupplierDTO>();

            DataContext = this;

            Loaded += ProductEditWindow_Loaded;
        }

        private async void ProductEditWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load categories and suppliers with explicit types
                IEnumerable<CategoryDTO> categories = await _categoryService.GetActiveAsync();
                IEnumerable<SupplierDTO> suppliers = await _supplierService.GetActiveAsync();

                Categories.Clear();
                foreach (CategoryDTO category in categories)
                {
                    Categories.Add(category);
                }

                Suppliers.Clear();
                // Add empty option for supplier - create a special DTO for the "no supplier" option
                SupplierDTO noSupplierOption = new SupplierDTO
                {
                    SupplierId = 0, // Use 0 instead of null to avoid nullable issues
                    Name = "-- No Supplier --"
                };
                Suppliers.Add(noSupplierOption);

                foreach (SupplierDTO supplier in suppliers)
                {
                    Suppliers.Add(supplier);
                }

                // Handle the case where the product has no supplier (null SupplierId)
                if (EditingProduct.SupplierId == null)
                {
                    EditingProduct.SupplierId = 0; // Set to 0 to match our "no supplier" option
                }

                // Trigger property change notifications to update UI
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(Barcode));
                OnPropertyChanged(nameof(BoxBarcode));
                OnPropertyChanged(nameof(CategoryId));
                OnPropertyChanged(nameof(SupplierId));
                OnPropertyChanged(nameof(PurchasePrice));
                OnPropertyChanged(nameof(WholesalePrice));
                OnPropertyChanged(nameof(SalePrice));
                OnPropertyChanged(nameof(CurrentStock));
                OnPropertyChanged(nameof(MinimumStock));
                OnPropertyChanged(nameof(ItemsPerBox));
                OnPropertyChanged(nameof(NumberOfBoxes));
                OnPropertyChanged(nameof(BoxPurchasePrice));
                OnPropertyChanged(nameof(BoxWholesalePrice));
                OnPropertyChanged(nameof(BoxSalePrice));
                OnPropertyChanged(nameof(MinimumBoxStock));
                OnPropertyChanged(nameof(Speed));
                OnPropertyChanged(nameof(ImagePath));
                OnPropertyChanged(nameof(IsActive));

                ValidateProduct();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ProductDTO CreateProductCopy(ProductDTO original)
        {
            return new ProductDTO
            {
                ProductId = original.ProductId,
                Name = original.Name,
                Description = original.Description,
                Barcode = original.Barcode,
                BoxBarcode = original.BoxBarcode,
                CategoryId = original.CategoryId,
                CategoryName = original.CategoryName,
                SupplierId = original.SupplierId,
                SupplierName = original.SupplierName,
                PurchasePrice = original.PurchasePrice,
                WholesalePrice = original.WholesalePrice,
                SalePrice = original.SalePrice,
                CurrentStock = original.CurrentStock,
                MinimumStock = original.MinimumStock,
                ItemsPerBox = original.ItemsPerBox,
                NumberOfBoxes = original.NumberOfBoxes,
                BoxPurchasePrice = original.BoxPurchasePrice,
                BoxWholesalePrice = original.BoxWholesalePrice,
                BoxSalePrice = original.BoxSalePrice,
                MinimumBoxStock = original.MinimumBoxStock,
                Speed = original.Speed,
                ImagePath = original.ImagePath,
                IsActive = original.IsActive,
                MainStockId = original.MainStockId,
                CreatedAt = original.CreatedAt,
                UpdatedAt = original.UpdatedAt,
                BarcodeImage = original.BarcodeImage
            };
        }

        private void ValidateProduct()
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Name))
                errors.Add("Product name is required.");

            if (string.IsNullOrWhiteSpace(Barcode))
                errors.Add("Barcode is required.");

            if (CategoryId <= 0)
                errors.Add("Category is required.");

            if (PurchasePrice < 0)
                errors.Add("Purchase price cannot be negative.");

            if (SalePrice < 0)
                errors.Add("Sale price cannot be negative.");

            if (CurrentStock < 0)
                errors.Add("Current stock cannot be negative.");

            if (MinimumStock < 0)
                errors.Add("Minimum stock cannot be negative.");

            if (ItemsPerBox <= 0)
                errors.Add("Items per box must be greater than zero.");

            HasValidationErrors = errors.Any();
            ValidationMessage = string.Join(Environment.NewLine, errors);
        }

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Title = "Select Product Image",
                    Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*",
                    Multiselect = false
                };

                bool? result = openFileDialog.ShowDialog();
                if (result == true)
                {
                    ImagePath = openFileDialog.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error selecting image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanSave)
                return;

            try
            {
                IsSaving = true;

                // Validate again before saving
                ValidateProduct();
                if (HasValidationErrors)
                {
                    MessageBox.Show("Please fix the validation errors before saving.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check if barcode is unique (excluding current product)
                ProductDTO existingProduct = await _productService.FindProductByBarcodeAsync(EditingProduct.Barcode, EditingProduct.ProductId);
                if (existingProduct != null)
                {
                    MessageBox.Show($"A product with barcode '{EditingProduct.Barcode}' already exists.", "Duplicate Barcode",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Handle the supplier selection - convert 0 back to null if "no supplier" was selected
                if (EditingProduct.SupplierId == 0)
                {
                    EditingProduct.SupplierId = null;
                    EditingProduct.SupplierName = string.Empty;
                }

                // Set update timestamp
                EditingProduct.UpdatedAt = DateTime.Now;

                // Save the product - Fixed: Properly handle the async call
                ProductDTO updatedProduct = await _productService.UpdateAsync(EditingProduct);

                MessageBox.Show("Product updated successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving product: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}