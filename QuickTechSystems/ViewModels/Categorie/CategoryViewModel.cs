using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.ViewModels.Categorie
{
    public class CategoryViewModel : ViewModelBase
    {
        private readonly ICategoryService _categoryService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _uiUpdateLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, DateTime> _lastEventTime = new Dictionary<string, DateTime>();
        private readonly Dictionary<int, DateTime> _operationTimestamps = new Dictionary<int, DateTime>();
        private readonly HashSet<int> _pendingOperations = new HashSet<int>();
        private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);
        private volatile bool _disposed = false;
        private int _loadingSequence = 0;

        private ObservableCollection<CategoryDTO> _productCategories;
        private ObservableCollection<CategoryDTO> _expenseCategories;
        private CategoryDTO? _selectedProductCategory;
        private CategoryDTO? _selectedExpenseCategory;
        private bool _isEditingProduct;
        private bool _isEditingExpense;
        private bool _isLoading;
        private string _loadingMessage;
        private Dictionary<string, string> _validationErrors;

        public CategoryViewModel(
            ICategoryService categoryService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _categoryService = categoryService;
            _productCategories = new ObservableCollection<CategoryDTO>();
            _expenseCategories = new ObservableCollection<CategoryDTO>();
            _validationErrors = new Dictionary<string, string>();
            _loadingMessage = "";

            InitializeCommands();
            LoadDataAsync();
        }

        private void InitializeCommands()
        {
            AddProductCommand = new RelayCommand(_ => AddProduct());
            AddExpenseCommand = new RelayCommand(_ => AddExpense());
            SaveProductCommand = new AsyncRelayCommand(async _ => await ExecuteWithLockAsync(SaveProductAsync));
            SaveExpenseCommand = new AsyncRelayCommand(async _ => await ExecuteWithLockAsync(SaveExpenseAsync));
            DeleteProductCommand = new AsyncRelayCommand(async _ => await ExecuteWithLockAsync(DeleteProductAsync));
            DeleteExpenseCommand = new AsyncRelayCommand(async _ => await ExecuteWithLockAsync(DeleteExpenseAsync));
            RefreshCommand = new AsyncRelayCommand(async _ => await ExecuteWithLockAsync(ForceRefreshAsync));
        }

        private async Task ExecuteWithLockAsync(Func<Task> operation)
        {
            if (_disposed) return;

            if (!await _operationLock.WaitAsync(100))
            {
                ShowTemporaryErrorMessage("Another operation is in progress. Please wait.");
                return;
            }

            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Operation error", ex);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public ObservableCollection<CategoryDTO> ProductCategories
        {
            get => _productCategories;
            set => SetProperty(ref _productCategories, value);
        }

        public ObservableCollection<CategoryDTO> ExpenseCategories
        {
            get => _expenseCategories;
            set => SetProperty(ref _expenseCategories, value);
        }

        public CategoryDTO? SelectedProductCategory
        {
            get => _selectedProductCategory;
            set
            {
                SetProperty(ref _selectedProductCategory, value);
                IsEditingProduct = value != null;
            }
        }

        public CategoryDTO? SelectedExpenseCategory
        {
            get => _selectedExpenseCategory;
            set
            {
                SetProperty(ref _selectedExpenseCategory, value);
                IsEditingExpense = value != null;
            }
        }

        public bool IsEditingProduct
        {
            get => _isEditingProduct;
            set => SetProperty(ref _isEditingProduct, value);
        }

        public bool IsEditingExpense
        {
            get => _isEditingExpense;
            set => SetProperty(ref _isEditingExpense, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        public Dictionary<string, string> ValidationErrors
        {
            get => _validationErrors;
            set => SetProperty(ref _validationErrors, value);
        }

        public ICommand AddProductCommand { get; private set; }
        public ICommand AddExpenseCommand { get; private set; }
        public ICommand SaveProductCommand { get; private set; }
        public ICommand SaveExpenseCommand { get; private set; }
        public ICommand DeleteProductCommand { get; private set; }
        public ICommand DeleteExpenseCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<CategoryDTO>>(HandleCategoryChanged);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(HandleCategoryChanged);
        }

        private async void HandleCategoryChanged(EntityChangedEvent<CategoryDTO> evt)
        {
            if (_disposed || !ShouldProcessEvent($"CategoryChanged_{evt.Action}_{evt.Entity.CategoryId}"))
                return;

            try
            {
                await Task.Delay(200);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling category change: {ex.Message}");
            }
        }

        private bool ShouldProcessEvent(string eventKey)
        {
            DateTime now = DateTime.Now;
            if (_lastEventTime.TryGetValue(eventKey, out DateTime lastTime))
            {
                if (now - lastTime < _debounceDelay)
                {
                    return false;
                }
            }

            _lastEventTime[eventKey] = now;
            return true;
        }

        protected override async Task LoadDataAsync()
        {
            if (_disposed || IsLoading)
                return;

            var currentSequence = Interlocked.Increment(ref _loadingSequence);

            try
            {
                IsLoading = true;
                LoadingMessage = "Loading categories...";

                var productCategories = await _categoryService.GetByTypeAsync("Product");
                var expenseCategories = await _categoryService.GetByTypeAsync("Expense");

                if (currentSequence != _loadingSequence)
                    return;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || currentSequence != _loadingSequence)
                        return;

                    ProductCategories = new ObservableCollection<CategoryDTO>(productCategories);
                    ExpenseCategories = new ObservableCollection<CategoryDTO>(expenseCategories);

                    OnPropertyChanged(nameof(ProductCategories));
                    OnPropertyChanged(nameof(ExpenseCategories));
                });
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error loading categories: {ex.Message}");
            }
            finally
            {
                if (currentSequence == _loadingSequence)
                {
                    IsLoading = false;
                }
            }
        }

        private async Task ForceRefreshAsync()
        {
            await LoadDataAsync();
        }

        public void AddProduct()
        {
            var newCategory = new CategoryDTO
            {
                Type = "Product",
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            OpenCategoryDetailsWindow(newCategory, "Product", true);
        }

        public void AddExpense()
        {
            var newCategory = new CategoryDTO
            {
                Type = "Expense",
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            OpenCategoryDetailsWindow(newCategory, "Expense", true);
        }

        public void EditProductCategory(CategoryDTO category)
        {
            if (category != null)
            {
                SelectedProductCategory = CreateCategoryClone(category);
                OpenCategoryDetailsWindow(SelectedProductCategory, "Product", false);
            }
        }

        public void EditExpenseCategory(CategoryDTO category)
        {
            if (category != null)
            {
                SelectedExpenseCategory = CreateCategoryClone(category);
                OpenCategoryDetailsWindow(SelectedExpenseCategory, "Expense", false);
            }
        }

        private async void OpenCategoryDetailsWindow(CategoryDTO category, string categoryType, bool isNew)
        {
            var categoryCopy = CreateCategoryClone(category);

            var window = new CategoryDetailsWindow(categoryCopy, categoryType, isNew);
            var result = window.ShowDialog();

            if (result == true)
            {
                category.Name = categoryCopy.Name;
                category.Description = categoryCopy.Description;
                category.IsActive = categoryCopy.IsActive;

                if (categoryType == "Product")
                {
                    SelectedProductCategory = category;
                    await ExecuteWithLockAsync(SaveProductAsync);
                }
                else
                {
                    SelectedExpenseCategory = category;
                    await ExecuteWithLockAsync(SaveExpenseAsync);
                }
            }
        }

        private async Task SaveProductAsync()
        {
            if (_disposed || !ValidateCategory(SelectedProductCategory))
                return;

            var operationId = SelectedProductCategory.CategoryId;
            if (_pendingOperations.Contains(operationId))
                return;

            _pendingOperations.Add(operationId);
            _operationTimestamps[operationId] = DateTime.Now;

            try
            {
                IsLoading = true;
                LoadingMessage = SelectedProductCategory.CategoryId == 0 ?
                    "Creating new product category..." :
                    "Updating product category...";

                var categoryBeingSaved = CreateCategoryClone(SelectedProductCategory);
                bool isNew = categoryBeingSaved.CategoryId == 0;

                if (isNew)
                {
                    categoryBeingSaved.CreatedAt = DateTime.Now;
                    var savedCategory = await _categoryService.CreateAsync(categoryBeingSaved);
                    await ShowSuccessMessage("Product category created successfully.");
                }
                else
                {
                    categoryBeingSaved.UpdatedAt = DateTime.Now;
                    await _categoryService.UpdateAsync(categoryBeingSaved);
                    await ShowSuccessMessage("Product category updated successfully.");
                }

                await ForceRefreshAsync();
                SelectedProductCategory = null;
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error saving product category: {ex.Message}");
            }
            finally
            {
                _pendingOperations.Remove(operationId);
                _operationTimestamps.Remove(operationId);
                IsLoading = false;
            }
        }

        private async Task SaveExpenseAsync()
        {
            if (_disposed || !ValidateCategory(SelectedExpenseCategory))
                return;

            var operationId = SelectedExpenseCategory.CategoryId;
            if (_pendingOperations.Contains(operationId))
                return;

            _pendingOperations.Add(operationId);
            _operationTimestamps[operationId] = DateTime.Now;

            try
            {
                IsLoading = true;
                LoadingMessage = SelectedExpenseCategory.CategoryId == 0 ?
                    "Creating new expense category..." :
                    "Updating expense category...";

                var categoryBeingSaved = CreateCategoryClone(SelectedExpenseCategory);
                bool isNew = categoryBeingSaved.CategoryId == 0;

                if (isNew)
                {
                    categoryBeingSaved.CreatedAt = DateTime.Now;
                    var savedCategory = await _categoryService.CreateAsync(categoryBeingSaved);
                    await ShowSuccessMessage("Expense category created successfully.");
                }
                else
                {
                    categoryBeingSaved.UpdatedAt = DateTime.Now;
                    await _categoryService.UpdateAsync(categoryBeingSaved);
                    await ShowSuccessMessage("Expense category updated successfully.");
                }

                await ForceRefreshAsync();
                SelectedExpenseCategory = null;
            }
            catch (Exception ex)
            {
                ShowTemporaryErrorMessage($"Error saving expense category: {ex.Message}");
            }
            finally
            {
                _pendingOperations.Remove(operationId);
                _operationTimestamps.Remove(operationId);
                IsLoading = false;
            }
        }

        private async Task DeleteProductAsync()
        {
            if (_disposed || SelectedProductCategory == null)
                return;

            var operationId = SelectedProductCategory.CategoryId;
            if (_pendingOperations.Contains(operationId))
                return;

            var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                return MessageBox.Show("Are you sure you want to delete this product category?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            });

            if (result != MessageBoxResult.Yes)
                return;

            _pendingOperations.Add(operationId);
            _operationTimestamps[operationId] = DateTime.Now;

            try
            {
                IsLoading = true;
                LoadingMessage = "Deleting product category...";

                int categoryId = SelectedProductCategory.CategoryId;

                await _categoryService.DeleteAsync(categoryId);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;

                    var categoryToRemove = ProductCategories.FirstOrDefault(c => c.CategoryId == categoryId);
                    if (categoryToRemove != null)
                    {
                        ProductCategories.Remove(categoryToRemove);
                    }
                });

                await ForceRefreshAsync();
                SelectedProductCategory = null;
                await ShowSuccessMessage("Product category deleted successfully.");
            }
            catch (Exception ex)
            {
                await HandleCategoryDeletionError(ex, SelectedProductCategory);
            }
            finally
            {
                _pendingOperations.Remove(operationId);
                _operationTimestamps.Remove(operationId);
                IsLoading = false;
            }
        }

        private async Task DeleteExpenseAsync()
        {
            if (_disposed || SelectedExpenseCategory == null)
                return;

            var operationId = SelectedExpenseCategory.CategoryId;
            if (_pendingOperations.Contains(operationId))
                return;

            var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                return MessageBox.Show("Are you sure you want to delete this expense category?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            });

            if (result != MessageBoxResult.Yes)
                return;

            _pendingOperations.Add(operationId);
            _operationTimestamps[operationId] = DateTime.Now;

            try
            {
                IsLoading = true;
                LoadingMessage = "Deleting expense category...";

                int categoryId = SelectedExpenseCategory.CategoryId;

                await _categoryService.DeleteAsync(categoryId);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;

                    var categoryToRemove = ExpenseCategories.FirstOrDefault(c => c.CategoryId == categoryId);
                    if (categoryToRemove != null)
                    {
                        ExpenseCategories.Remove(categoryToRemove);
                    }
                });

                await ForceRefreshAsync();
                SelectedExpenseCategory = null;
                await ShowSuccessMessage("Expense category deleted successfully.");
            }
            catch (Exception ex)
            {
                await HandleCategoryDeletionError(ex, SelectedExpenseCategory);
            }
            finally
            {
                _pendingOperations.Remove(operationId);
                _operationTimestamps.Remove(operationId);
                IsLoading = false;
            }
        }

        private async Task HandleCategoryDeletionError(Exception ex, CategoryDTO category)
        {
            if (ex.Message.Contains("references") || ex.Message.Contains("constraint") || ex.Message.Contains("associated"))
            {
                ShowTemporaryErrorMessage("This category is in use and cannot be deleted. Consider marking it as inactive instead.");

                var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    return MessageBox.Show("Would you like to mark this category as inactive instead?",
                        "Mark as Inactive", MessageBoxButton.YesNo, MessageBoxImage.Question);
                });

                if (result == MessageBoxResult.Yes && category != null)
                {
                    category.IsActive = false;
                    if (category.Type == "Product")
                    {
                        SelectedProductCategory = category;
                        await SaveProductAsync();
                    }
                    else
                    {
                        SelectedExpenseCategory = category;
                        await SaveExpenseAsync();
                    }
                }
            }
            else
            {
                ShowTemporaryErrorMessage($"Error deleting category: {ex.Message}");
            }
        }

        private bool ValidateCategory(CategoryDTO? category)
        {
            ValidationErrors.Clear();

            if (category == null)
            {
                ValidationErrors.Add("General", "No category selected.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(category.Name))
                ValidationErrors.Add("Name", "Category name is required.");

            if (category.Name?.Length > 100)
                ValidationErrors.Add("Name", "Category name cannot exceed 100 characters.");

            if (category.Description?.Length > 500)
                ValidationErrors.Add("Description", "Description cannot exceed 500 characters.");

            OnPropertyChanged(nameof(ValidationErrors));

            if (ValidationErrors.Count > 0)
            {
                ShowValidationErrors(ValidationErrors.Values.ToList());
                return false;
            }

            return true;
        }

        private CategoryDTO CreateCategoryClone(CategoryDTO source)
        {
            return new CategoryDTO
            {
                CategoryId = source.CategoryId,
                Name = source.Name,
                Description = source.Description,
                Type = source.Type,
                IsActive = source.IsActive,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                ProductCount = source.ProductCount
            };
        }

        private void ShowValidationErrors(List<string> errors)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(string.Join("\n", errors), "Validation Errors",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        private async Task ShowSuccessMessage(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void ShowTemporaryErrorMessage(string message)
        {
            LoadingMessage = message;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });

            Task.Run(async () =>
            {
                await Task.Delay(3000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (LoadingMessage == message)
                    {
                        LoadingMessage = string.Empty;
                    }
                });
            });
        }

        private async Task HandleExceptionAsync(string context, Exception ex)
        {
            Debug.WriteLine($"{context}: {ex.Message}");

            string userMessage = ex.Message.Contains("Operation already in progress") ||
                               ex.Message.Contains("second operation")
                ? "Another operation is in progress. Please wait and try again."
                : ex.Message.Contains("already exists")
                ? ex.Message
                : ex.Message.Contains("not found")
                ? "The requested category was not found. Please refresh and try again."
                : $"An error occurred: {ex.Message}";

            ShowTemporaryErrorMessage(userMessage);
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _operationLock?.Dispose();
            _uiUpdateLock?.Dispose();
            UnsubscribeFromEvents();

            base.Dispose();
        }
    }
}