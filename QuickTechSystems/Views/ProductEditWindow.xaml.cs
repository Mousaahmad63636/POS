// Path: QuickTechSystems.WPF.Views/ProductEditWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    /// <summary>
    /// Interaction logic for ProductEditWindow.xaml
    /// </summary>
    public partial class ProductEditWindow : Window, INotifyPropertyChanged
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private readonly ProductDTO _originalProduct;

        private ProductDTO _editableProduct;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private bool _isSaving;
        private string _statusMessage = string.Empty;
        private Dictionary<string, List<string>> _validationErrors = new Dictionary<string, List<string>>();

        // Properties for data binding
        public ProductDTO EditableProduct
        {
            get => _editableProduct;
            set
            {
                _editableProduct = value;
                OnPropertyChanged();
                ValidateProduct();
            }
        }

        public ObservableCollection<CategoryDTO> Categories
        {
            get => _categories;
            set
            {
                _categories = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<SupplierDTO> Suppliers
        {
            get => _suppliers;
            set
            {
                _suppliers = value;
                OnPropertyChanged();
            }
        }

        public bool IsSaving
        {
            get => _isSaving;
            set
            {
                _isSaving = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotSaving));
            }
        }

        public bool IsNotSaving => !IsSaving;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public Dictionary<string, List<string>> ValidationErrors
        {
            get => _validationErrors;
            set
            {
                _validationErrors = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasValidationErrors));
                OnPropertyChanged(nameof(CanSave));
            }
        }

        public bool HasValidationErrors => ValidationErrors.Any(kvp => kvp.Value.Any());

        public bool CanSave => IsNotSaving && !HasValidationErrors && EditableProduct != null;

        // Commands
        public ICommand SaveCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand ResetCommand { get; private set; }

        /// <summary>
        /// Constructor for ProductEditWindow
        /// </summary>
        /// <param name="product">The product to edit</param>
        /// <param name="productService">Product service for data operations</param>
        /// <param name="categoryService">Category service for loading categories</param>
        /// <param name="supplierService">Supplier service for loading suppliers</param>
        public ProductEditWindow(
            ProductDTO product,
            IProductService productService,
            ICategoryService categoryService,
            ISupplierService supplierService)
        {
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _supplierService = supplierService ?? throw new ArgumentNullException(nameof(supplierService));
            _originalProduct = product ?? throw new ArgumentNullException(nameof(product));

            InitializeComponent();
            InitializeCommands();
            InitializeData();

            this.DataContext = this;
            this.Loaded += ProductEditWindow_Loaded;
        }

        private void InitializeCommands()
        {
            SaveCommand = new RelayCommand(async _ => await SaveProductAsync(), _ => CanSave);
            CancelCommand = new RelayCommand(_ => CancelEdit());
            ResetCommand = new RelayCommand(_ => ResetToOriginal());
        }

        private void InitializeData()
        {
            // Create a copy of the original product for editing
            EditableProduct = CreateProductCopy(_originalProduct);

            // Initialize collections
            Categories = new ObservableCollection<CategoryDTO>();
            Suppliers = new ObservableCollection<SupplierDTO>();
        }

        private async void ProductEditWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        /// <summary>
        /// Loads categories and suppliers from the services
        /// </summary>
        private async Task LoadDataAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Loading data...";

                // Load categories and suppliers concurrently
                var categoriesTask = _categoryService.GetActiveAsync();
                var suppliersTask = _supplierService.GetActiveAsync();

                await Task.WhenAll(categoriesTask, suppliersTask);

                var categories = await categoriesTask;
                var suppliers = await suppliersTask;

                // Update UI on main thread
                await Dispatcher.InvokeAsync(() =>
                {
                    Categories.Clear();
                    foreach (var category in categories)
                    {
                        Categories.Add(category);
                    }

                    Suppliers.Clear();
                    foreach (var supplier in suppliers)
                    {
                        Suppliers.Add(supplier);
                    }

                    SetCurrentSelections();
                });

                StatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading data: {ex.Message}";
                Debug.WriteLine($"Error in LoadDataAsync: {ex}");
            }
            finally
            {
                IsSaving = false;
            }
        }

        private void SetCurrentSelections()
        {
            // Ensure the current category and supplier are selected
            if (EditableProduct.CategoryId > 0)
            {
                var selectedCategory = Categories.FirstOrDefault(c => c.CategoryId == EditableProduct.CategoryId);
                if (selectedCategory != null)
                {
                    EditableProduct.CategoryName = selectedCategory.Name;
                }
            }

            if (EditableProduct.SupplierId.HasValue && EditableProduct.SupplierId > 0)
            {
                var selectedSupplier = Suppliers.FirstOrDefault(s => s.SupplierId == EditableProduct.SupplierId);
                if (selectedSupplier != null)
                {
                    EditableProduct.SupplierName = selectedSupplier.Name;
                }
            }
        }

        /// <summary>
        /// Creates a deep copy of the product for editing
        /// </summary>
        /// <param name="original">The original product</param>
        /// <returns>A copy of the product</returns>
        private ProductDTO CreateProductCopy(ProductDTO original)
        {
            return new ProductDTO
            {
                ProductId = original.ProductId,
                Name = original.Name,
                Barcode = original.Barcode,
                BoxBarcode = original.BoxBarcode,
                Description = original.Description,
                CategoryId = original.CategoryId,
                CategoryName = original.CategoryName,
                SupplierId = original.SupplierId,
                SupplierName = original.SupplierName,
                MainStockId = original.MainStockId,
                PurchasePrice = original.PurchasePrice,
                SalePrice = original.SalePrice,
                WholesalePrice = original.WholesalePrice,
                CurrentStock = original.CurrentStock,
                MinimumStock = original.MinimumStock,
                BoxPurchasePrice = original.BoxPurchasePrice,
                BoxSalePrice = original.BoxSalePrice,
                BoxWholesalePrice = original.BoxWholesalePrice,
                ItemsPerBox = original.ItemsPerBox,
                NumberOfBoxes = original.NumberOfBoxes,
                MinimumBoxStock = original.MinimumBoxStock,
                ImagePath = original.ImagePath,
                Speed = original.Speed,
                IsActive = original.IsActive,
                CreatedAt = original.CreatedAt,
                UpdatedAt = original.UpdatedAt,
                BarcodeImage = original.BarcodeImage
            };
        }

        /// <summary>
        /// Validates the current product data
        /// </summary>
        private void ValidateProduct()
        {
            var errors = new Dictionary<string, List<string>>();

            if (EditableProduct == null)
            {
                ValidationErrors = errors;
                return;
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(EditableProduct.Name))
            {
                AddValidationError(errors, nameof(EditableProduct.Name), "Product name is required.");
            }

            if (string.IsNullOrWhiteSpace(EditableProduct.Barcode))
            {
                AddValidationError(errors, nameof(EditableProduct.Barcode), "Barcode is required.");
            }

            if (EditableProduct.CategoryId <= 0)
            {
                AddValidationError(errors, nameof(EditableProduct.CategoryId), "Please select a category.");
            }

            // Validate prices
            if (EditableProduct.PurchasePrice < 0)
            {
                AddValidationError(errors, nameof(EditableProduct.PurchasePrice), "Purchase price cannot be negative.");
            }

            if (EditableProduct.SalePrice < 0)
            {
                AddValidationError(errors, nameof(EditableProduct.SalePrice), "Sale price cannot be negative.");
            }

            if (EditableProduct.WholesalePrice < 0)
            {
                AddValidationError(errors, nameof(EditableProduct.WholesalePrice), "Wholesale price cannot be negative.");
            }

            // Validate stock levels
            if (EditableProduct.CurrentStock < 0)
            {
                AddValidationError(errors, nameof(EditableProduct.CurrentStock), "Current stock cannot be negative.");
            }

            if (EditableProduct.MinimumStock < 0)
            {
                AddValidationError(errors, nameof(EditableProduct.MinimumStock), "Minimum stock cannot be negative.");
            }

            // Validate box-related fields
            if (EditableProduct.ItemsPerBox <= 0)
            {
                AddValidationError(errors, nameof(EditableProduct.ItemsPerBox), "Items per box must be greater than zero.");
            }

            if (EditableProduct.BoxPurchasePrice < 0)
            {
                AddValidationError(errors, nameof(EditableProduct.BoxPurchasePrice), "Box purchase price cannot be negative.");
            }

            if (EditableProduct.BoxSalePrice < 0)
            {
                AddValidationError(errors, nameof(EditableProduct.BoxSalePrice), "Box sale price cannot be negative.");
            }

            if (EditableProduct.BoxWholesalePrice < 0)
            {
                AddValidationError(errors, nameof(EditableProduct.BoxWholesalePrice), "Box wholesale price cannot be negative.");
            }

            if (EditableProduct.NumberOfBoxes < 0)
            {
                AddValidationError(errors, nameof(EditableProduct.NumberOfBoxes), "Number of boxes cannot be negative.");
            }

            ValidationErrors = errors;
        }

        /// <summary>
        /// Adds a validation error to the errors dictionary
        /// </summary>
        private void AddValidationError(Dictionary<string, List<string>> errors, string propertyName, string errorMessage)
        {
            if (!errors.ContainsKey(propertyName))
            {
                errors[propertyName] = new List<string>();
            }
            errors[propertyName].Add(errorMessage);
        }

        /// <summary>
        /// Saves the product changes
        /// </summary>
        private async Task SaveProductAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Saving product...";

                // Validate before saving
                ValidateProduct();
                if (HasValidationErrors)
                {
                    StatusMessage = "Please fix validation errors before saving.";
                    return;
                }

                // Check for barcode uniqueness if it changed
                if (EditableProduct.Barcode != _originalProduct.Barcode)
                {
                    var existingProduct = await _productService.FindProductByBarcodeAsync(
                        EditableProduct.Barcode,
                        EditableProduct.ProductId);

                    if (existingProduct != null)
                    {
                        StatusMessage = "A product with this barcode already exists.";
                        MessageBox.Show("A product with this barcode already exists. Please use a different barcode.",
                            "Duplicate Barcode", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Update the editable product's timestamp
                EditableProduct.UpdatedAt = DateTime.Now;

                // Save the product
                var updatedProduct = await _productService.UpdateAsync(EditableProduct);

                if (updatedProduct != null)
                {
                    StatusMessage = "Product saved successfully!";

                    // Show success message and close the window
                    MessageBox.Show("Product updated successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    DialogResult = true;
                    Close();
                }
                else
                {
                    StatusMessage = "Failed to save product.";
                    MessageBox.Show("Failed to save the product. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving product: {ex.Message}";
                Debug.WriteLine($"Error in SaveProductAsync: {ex}");

                MessageBox.Show($"An error occurred while saving the product:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
                if (StatusMessage.StartsWith("Product saved"))
                {
                    // Clear success message after a delay
                    await Task.Delay(2000);
                    if (StatusMessage.StartsWith("Product saved"))
                    {
                        StatusMessage = string.Empty;
                    }
                }
            }
        }

        /// <summary>
        /// Cancels the edit operation
        /// </summary>
        private void CancelEdit()
        {
            if (HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Are you sure you want to cancel?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Resets the editable product to the original values
        /// </summary>
        private void ResetToOriginal()
        {
            var result = MessageBox.Show(
                "Are you sure you want to reset all changes?",
                "Reset Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                EditableProduct = CreateProductCopy(_originalProduct);
                StatusMessage = "Changes reset to original values.";

                // Clear the status message after a delay
                Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (StatusMessage == "Changes reset to original values.")
                        {
                            StatusMessage = string.Empty;
                        }
                    });
                });
            }
        }

        /// <summary>
        /// Checks if there are unsaved changes
        /// </summary>
        /// <returns>True if there are unsaved changes</returns>
        private bool HasUnsavedChanges()
        {
            if (EditableProduct == null || _originalProduct == null)
                return false;

            return EditableProduct.Name != _originalProduct.Name ||
                   EditableProduct.Barcode != _originalProduct.Barcode ||
                   EditableProduct.BoxBarcode != _originalProduct.BoxBarcode ||
                   EditableProduct.Description != _originalProduct.Description ||
                   EditableProduct.CategoryId != _originalProduct.CategoryId ||
                   EditableProduct.SupplierId != _originalProduct.SupplierId ||
                   EditableProduct.PurchasePrice != _originalProduct.PurchasePrice ||
                   EditableProduct.SalePrice != _originalProduct.SalePrice ||
                   EditableProduct.WholesalePrice != _originalProduct.WholesalePrice ||
                   EditableProduct.CurrentStock != _originalProduct.CurrentStock ||
                   EditableProduct.MinimumStock != _originalProduct.MinimumStock ||
                   EditableProduct.BoxPurchasePrice != _originalProduct.BoxPurchasePrice ||
                   EditableProduct.BoxSalePrice != _originalProduct.BoxSalePrice ||
                   EditableProduct.BoxWholesalePrice != _originalProduct.BoxWholesalePrice ||
                   EditableProduct.ItemsPerBox != _originalProduct.ItemsPerBox ||
                   EditableProduct.NumberOfBoxes != _originalProduct.NumberOfBoxes ||
                   EditableProduct.MinimumBoxStock != _originalProduct.MinimumBoxStock ||
                   EditableProduct.ImagePath != _originalProduct.ImagePath ||
                   EditableProduct.Speed != _originalProduct.Speed ||
                   EditableProduct.IsActive != _originalProduct.IsActive;
        }

        /// <summary>
        /// Handles the category selection change
        /// </summary>
        private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is CategoryDTO selectedCategory)
            {
                EditableProduct.CategoryId = selectedCategory.CategoryId;
                EditableProduct.CategoryName = selectedCategory.Name;
                ValidateProduct();
            }
        }

        /// <summary>
        /// Handles the supplier selection change
        /// </summary>
        private void OnSupplierSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is SupplierDTO selectedSupplier)
            {
                EditableProduct.SupplierId = selectedSupplier.SupplierId;
                EditableProduct.SupplierName = selectedSupplier.Name;
            }
            else
            {
                EditableProduct.SupplierId = null;
                EditableProduct.SupplierName = string.Empty;
            }
        }

        /// <summary>
        /// Handles property changed events for data binding
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Handles the window closing event
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (DialogResult != true && HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Are you sure you want to close?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }

        /// <summary>
        /// Simple RelayCommand implementation for this window
        /// </summary>
        private class RelayCommand : ICommand
        {
            private readonly Func<object, Task> _executeAsync;
            private readonly Action<object> _execute;
            private readonly Predicate<object> _canExecute;

            public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public RelayCommand(Func<object, Task> executeAsync, Predicate<object> canExecute = null)
            {
                _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
                _canExecute = canExecute;
            }

            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }

            public bool CanExecute(object parameter)
            {
                return _canExecute?.Invoke(parameter) ?? true;
            }

            public async void Execute(object parameter)
            {
                if (_executeAsync != null)
                {
                    await _executeAsync(parameter);
                }
                else
                {
                    _execute(parameter);
                }
            }
        }
    }
}