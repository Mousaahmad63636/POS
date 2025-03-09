using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.WPF.ViewModels
{
    public class ExpenseViewModel : ViewModelBase
    {
        private readonly IExpenseService _expenseService;
        private readonly IDrawerService _drawerService;
        private readonly ICategoryService _categoryService;
        private ObservableCollection<ExpenseDTO> _expenses;
        private ExpenseDTO? _selectedExpense;
        private ExpenseDTO _currentExpense;
        private Action<EntityChangedEvent<ExpenseDTO>> _expenseChangedHandler;
        private ObservableCollection<string> _categories;
        private string _selectedCategory;
        private DateTime _filterStartDate = DateTime.Today;
        private ObservableCollection<CategorySummary> _categorySummaries;
        private bool _isLoading;
        private bool _isExpensePopupOpen;
        private bool _isNewExpense;

        // Concurrency control
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _operationInProgress = false;

        public ExpenseViewModel(
          IExpenseService expenseService,
          IDrawerService drawerService,
          ICategoryService categoryService,
          IEventAggregator eventAggregator) : base(eventAggregator)
        {
            Debug.WriteLine("=== Initializing ExpenseViewModel ===");

            _expenseService = expenseService ?? throw new ArgumentNullException(nameof(expenseService));
            _drawerService = drawerService ?? throw new ArgumentNullException(nameof(drawerService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _expenses = new ObservableCollection<ExpenseDTO>();
            _currentExpense = new ExpenseDTO { Date = DateTime.Today };
            _expenseChangedHandler = HandleExpenseChanged;
            _categorySummaries = new ObservableCollection<CategorySummary>();
            _categories = new ObservableCollection<string>();

            // Set filter start date to beginning of month to see more expenses
            _filterStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            _isLoading = false;
            _isExpensePopupOpen = false;
            _isNewExpense = false;

            Debug.WriteLine("Setting up commands...");
            DeleteCommand = new AsyncRelayCommand(async param => await DeleteAsync(param as ExpenseDTO));
            EditCommand = new RelayCommand(param => EditExpense(param as ExpenseDTO));
            ClearCommand = new RelayCommand(_ => ClearForm());
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
            ApplyFilterCommand = new RelayCommand(_ => ApplyFilter());

            Debug.WriteLine("Starting initial data load...");

            // Initialize data asynchronously with delay to prevent concurrency issues
            Task.Run(async () => {
                // Wait a moment to ensure other initialization is complete
                await Task.Delay(500);
                await LoadCategoriesAsync();
                await Task.Delay(300);
                await EnsureExpensesLoadedAsync();
            });

            Debug.WriteLine("ExpenseViewModel initialization complete");
        }

        // New method to ensure expenses are loaded with retries
        private async Task EnsureExpensesLoadedAsync()
        {
            Debug.WriteLine("=== BEGIN EnsureExpensesLoadedAsync ===");
            int attempts = 0;
            const int maxAttempts = 3;

            while (attempts < maxAttempts)
            {
                attempts++;
                try
                {
                    Debug.WriteLine($"Attempt {attempts} to load expenses...");
                    await LoadDataAsync();

                    // Check if we have expenses
                    if (Expenses != null && Expenses.Any())
                    {
                        Debug.WriteLine($"Successfully loaded {Expenses.Count} expenses");
                        break;
                    }
                    else
                    {
                        Debug.WriteLine("No expenses loaded, will retry after delay");
                        await Task.Delay(1000 * attempts); // Increasing delay between attempts
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading expenses on attempt {attempts}: {ex.Message}");
                    if (attempts < maxAttempts)
                    {
                        await Task.Delay(1000 * attempts);
                    }
                }
            }

            Debug.WriteLine("=== END EnsureExpensesLoadedAsync ===");
        }

        // Helper method for safe database operations
        private async Task<T> ExecuteDbOperationSafelyAsync<T>(Func<Task<T>> operation, string operationName = "Database operation")
        {
            Debug.WriteLine($"BEGIN: {operationName}");

            // If an operation is already in progress, wait a bit
            int waitCount = 0;
            while (_operationInProgress)
            {
                waitCount++;
                Debug.WriteLine($"Operation in progress, waiting... (attempt {waitCount})");
                await Task.Delay(100);

                // Safety timeout
                if (waitCount > 50) // 5 seconds max wait
                {
                    Debug.WriteLine("TIMEOUT waiting for operation lock, proceeding anyway");
                    break;
                }
            }

            Debug.WriteLine($"Acquiring operation lock for: {operationName}");
            await _operationLock.WaitAsync();
            _operationInProgress = true;

            try
            {
                Debug.WriteLine($"Executing operation: {operationName}");
                // Add a small delay to ensure any previous operation is fully complete
                await Task.Delay(200);
                var result = await operation();
                Debug.WriteLine($"Operation completed successfully: {operationName}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in {operationName}: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await ShowErrorMessageAsync($"Error: {ex.Message}");
                throw;
            }
            finally
            {
                _operationInProgress = false;
                _operationLock.Release();
                Debug.WriteLine($"Released operation lock for: {operationName}");
                Debug.WriteLine($"END: {operationName}");
            }
        }

        // Overload for void operations
        private async Task ExecuteDbOperationSafelyAsync(Func<Task> operation, string operationName = "Database operation")
        {
            await ExecuteDbOperationSafelyAsync<bool>(async () =>
            {
                await operation();
                return true;
            }, operationName);
        }

        // Properties
        public ObservableCollection<string> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                Debug.WriteLine($"SelectedCategory changing from '{_selectedCategory}' to '{value}'");
                if (SetProperty(ref _selectedCategory, value))
                {
                    Debug.WriteLine("SelectedCategory changed, applying filter");
                    ApplyFilter();
                }
            }
        }

        public ObservableCollection<ExpenseDTO> Expenses
        {
            get => _expenses;
            set
            {
                Debug.WriteLine($"Setting Expenses collection with {value?.Count ?? 0} items");
                SetProperty(ref _expenses, value);
            }
        }

        public ExpenseDTO? SelectedExpense
        {
            get => _selectedExpense;
            set => SetProperty(ref _selectedExpense, value);
        }

        public ExpenseDTO CurrentExpense
        {
            get => _currentExpense;
            set => SetProperty(ref _currentExpense, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                Debug.WriteLine($"IsLoading changing to: {value}");
                SetProperty(ref _isLoading, value);
            }
        }

        public DateTime FilterStartDate
        {
            get => _filterStartDate;
            set
            {
                Debug.WriteLine($"FilterStartDate changing to: {value:d}");
                if (SetProperty(ref _filterStartDate, value))
                {
                    Debug.WriteLine("FilterStartDate changed, applying filter");
                    ApplyFilter();
                }
            }
        }

        public ObservableCollection<CategorySummary> CategorySummaries
        {
            get => _categorySummaries;
            set
            {
                Debug.WriteLine($"Setting CategorySummaries with {value?.Count ?? 0} items");
                SetProperty(ref _categorySummaries, value);
            }
        }

        public bool IsExpensePopupOpen
        {
            get => _isExpensePopupOpen;
            set => SetProperty(ref _isExpensePopupOpen, value);
        }

        public bool IsNewExpense
        {
            get => _isNewExpense;
            set => SetProperty(ref _isNewExpense, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ApplyFilterCommand { get; }

        // Event handling methods
        protected override void SubscribeToEvents()
        {
            Debug.WriteLine("Subscribing to events...");
            base.SubscribeToEvents();
            _eventAggregator.Subscribe<EntityChangedEvent<CategoryDTO>>(HandleCategoryChanged);
            _eventAggregator.Subscribe<EntityChangedEvent<ExpenseDTO>>(_expenseChangedHandler);
            Debug.WriteLine("Event subscriptions complete");
        }

        protected override void UnsubscribeFromEvents()
        {
            Debug.WriteLine("Unsubscribing from events...");
            base.UnsubscribeFromEvents();
            _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(HandleCategoryChanged);
            _eventAggregator.Unsubscribe<EntityChangedEvent<ExpenseDTO>>(_expenseChangedHandler);
            Debug.WriteLine("Event unsubscriptions complete");
        }

        private async void HandleCategoryChanged(EntityChangedEvent<CategoryDTO> evt)
        {
            Debug.WriteLine($"CategoryChanged event received: {evt.Action} - {evt.Entity.Name}");
            try
            {
                await LoadCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling category change: {ex.Message}");
            }
        }

        private async void HandleExpenseChanged(EntityChangedEvent<ExpenseDTO> evt)
        {
            Debug.WriteLine($"ExpenseChanged event received: {evt.Action} - ID:{evt.Entity.ExpenseId}, Reason:{evt.Entity.Reason}");
            try
            {
                // Allow a delay before reloading to avoid concurrency issues
                Debug.WriteLine("Waiting before processing expense change...");
                await Task.Delay(300);
                Debug.WriteLine("Processing expense change by reloading data...");
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling expense change: {ex.Message}");
            }
        }

        // Data loading methods
        protected override async Task LoadDataAsync()
        {
            Debug.WriteLine("=== BEGIN LoadDataAsync ===");
            if (IsLoading)
            {
                Debug.WriteLine("Already loading data, skipping this request");
                return;
            }

            try
            {
                IsLoading = true;
                Debug.WriteLine("Loading expenses from database...");

                var expenses = await ExecuteDbOperationSafelyAsync(
                    () => _expenseService.GetAllAsync(),
                    "Loading expenses");

                Debug.WriteLine($"Successfully loaded {expenses.Count()} expenses from database");

                foreach (var expense in expenses)
                {
                    Debug.WriteLine($"Expense ID: {expense.ExpenseId}, Reason: {expense.Reason}, Amount: {expense.Amount}, Date: {expense.Date:d}, Category: {expense.Category}");
                }

                // Update UI on the dispatcher thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Debug.WriteLine("Updating UI with expenses...");
                    Expenses = new ObservableCollection<ExpenseDTO>(
                        expenses.OrderByDescending(e => e.Date));
                    Debug.WriteLine($"Expenses collection updated with {Expenses.Count} items");

                    ApplyFilter();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in LoadDataAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                IsLoading = false;
                Debug.WriteLine("=== END LoadDataAsync ===");
            }
        }

        private async Task LoadCategoriesAsync()
        {
            Debug.WriteLine("=== BEGIN LoadCategoriesAsync ===");
            try
            {
                IsLoading = true;
                Debug.WriteLine("Loading expense categories...");

                var expenseCategories = await ExecuteDbOperationSafelyAsync(
                    () => _categoryService.GetExpenseCategoriesAsync(),
                    "Loading expense categories");

                Debug.WriteLine($"Successfully loaded {expenseCategories.Count()} expense categories");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Debug.WriteLine("Updating UI with categories...");
                    Categories.Clear();

                    // Add an "All" option first
                    Categories.Add("All");

                    foreach (var category in expenseCategories.Where(c => c.IsActive))
                    {
                        Debug.WriteLine($"Adding category: {category.Name}");
                        Categories.Add(category.Name);
                    }

                    if (!Categories.Contains("Other"))
                    {
                        Debug.WriteLine("Adding 'Other' category");
                        Categories.Add("Other");
                    }

                    Debug.WriteLine($"Categories collection now has {Categories.Count} items");

                    // Set default selections
                    if (string.IsNullOrEmpty(SelectedCategory) && Categories.Any())
                    {
                        Debug.WriteLine("Setting default category to 'All'");
                        SelectedCategory = "All";
                    }

                    if (CurrentExpense?.Category == null && Categories.Count > 1)
                    {
                        Debug.WriteLine($"Setting current expense category to {Categories[1]}");
                        CurrentExpense.Category = Categories[1]; // Skip "All"
                    }

                    Debug.WriteLine("Applying filter with updated categories");
                    ApplyFilter();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in LoadCategoriesAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                IsLoading = false;
                Debug.WriteLine("=== END LoadCategoriesAsync ===");
            }
        }

        // Popup handling methods
        public void ShowExpensePopup()
        {
            IsExpensePopupOpen = true;
        }

        public void CloseExpensePopup()
        {
            IsExpensePopupOpen = false;
        }

        // UI event handlers
        private void ApplyFilter()
        {
            Debug.WriteLine("=== BEGIN ApplyFilter ===");
            // If no expenses, nothing to filter
            if (Expenses == null)
            {
                Debug.WriteLine("Expenses collection is null");
                CategorySummaries = new ObservableCollection<CategorySummary>();
                Debug.WriteLine("=== END ApplyFilter (early) ===");
                return;
            }

            if (!Expenses.Any())
            {
                Debug.WriteLine("No expenses to filter");
                CategorySummaries = new ObservableCollection<CategorySummary>();
                Debug.WriteLine("=== END ApplyFilter (early) ===");
                return;
            }

            // If "All" is selected or null, show all
            bool showAll = string.IsNullOrEmpty(SelectedCategory) || SelectedCategory == "All";
            Debug.WriteLine($"Filter criteria: Category = {(showAll ? "All" : SelectedCategory)}, StartDate = {FilterStartDate:d}");
            Debug.WriteLine($"Starting with {Expenses.Count} total expenses");

            var filtered = Expenses.Where(e =>
                (showAll || e.Category == SelectedCategory) &&
                e.Date >= FilterStartDate).ToList();

            Debug.WriteLine($"After filtering: {filtered.Count} expenses match criteria");

            foreach (var expense in filtered)
            {
                Debug.WriteLine($"Filtered expense: ID: {expense.ExpenseId}, Reason: {expense.Reason}, Amount: {expense.Amount}, Date: {expense.Date:d}, Category: {expense.Category}");
            }

            var summaries = filtered
                .GroupBy(e => e.Category)
                .Select(g => new CategorySummary
                {
                    CategoryName = g.Key,
                    Count = g.Count(),
                    TotalAmount = g.Sum(e => e.Amount)
                })
                .OrderBy(s => s.CategoryName)
                .ToList();

            Debug.WriteLine($"Created {summaries.Count} category summaries");

            foreach (var summary in summaries)
            {
                Debug.WriteLine($"Summary: {summary.CategoryName}, Count: {summary.Count}, Total: {summary.TotalAmount:C2}");
            }

            // Add a total row
            summaries.Add(new CategorySummary
            {
                CategoryName = "Total",
                Count = summaries.Sum(s => s.Count),
                TotalAmount = summaries.Sum(s => s.TotalAmount),
                IsTotal = true
            });

            Debug.WriteLine($"Final summary totals - Count: {summaries.Last().Count}, Amount: {summaries.Last().TotalAmount:C2}");

            CategorySummaries = new ObservableCollection<CategorySummary>(summaries);
            Debug.WriteLine($"Updated CategorySummaries with {CategorySummaries.Count} items");
            Debug.WriteLine("=== END ApplyFilter ===");
        }

        private async Task SaveAsync()
        {
            Debug.WriteLine("=== BEGIN SaveAsync ===");
            try
            {
                // Input validation - no DB access, so outside the safe execution block
                if (CurrentExpense == null)
                {
                    Debug.WriteLine("CurrentExpense is null, cannot save");
                    return;
                }

                Debug.WriteLine($"Validating expense - Reason: '{CurrentExpense.Reason}', Amount: {CurrentExpense.Amount}, Date: {CurrentExpense.Date:d}, Category: {CurrentExpense.Category}");

                if (string.IsNullOrWhiteSpace(CurrentExpense.Reason))
                {
                    Debug.WriteLine("Validation failed: Reason is empty");
                    MessageBox.Show("Please enter a reason for the expense.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (CurrentExpense.Amount <= 0)
                {
                    Debug.WriteLine("Validation failed: Amount is <= 0");
                    MessageBox.Show("Please enter a valid amount.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Prepare the expense to save (clone it to avoid modification during async operations)
                var expenseToSave = new ExpenseDTO
                {
                    ExpenseId = CurrentExpense.ExpenseId,
                    Reason = CurrentExpense.Reason,
                    Amount = CurrentExpense.Amount,
                    Date = CurrentExpense.Date,
                    Notes = CurrentExpense.Notes,
                    Category = CurrentExpense.Category,
                    IsRecurring = CurrentExpense.IsRecurring,
                    CreatedAt = CurrentExpense.ExpenseId == 0 ? DateTime.Now : CurrentExpense.CreatedAt,
                    UpdatedAt = CurrentExpense.ExpenseId != 0 ? DateTime.Now : null
                };

                Debug.WriteLine($"Created expense clone for saving: ID: {expenseToSave.ExpenseId}, Reason: {expenseToSave.Reason}, Amount: {expenseToSave.Amount:C2}");

                // Start loading UI indicator
                IsLoading = true;

                // Pre-check drawer balance
                Debug.WriteLine("Checking drawer balance");
                var drawer = await ExecuteDbOperationSafelyAsync(
                    () => _drawerService.GetCurrentDrawerAsync(),
                    "Checking drawer balance");

                if (drawer == null)
                {
                    Debug.WriteLine("No active drawer found");
                    MessageBox.Show("No active cash drawer found. Please open a drawer first.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    IsLoading = false;
                    return;
                }

                Debug.WriteLine($"Drawer found with balance: {drawer.CurrentBalance:C2}");

                if (expenseToSave.Amount > drawer.CurrentBalance)
                {
                    Debug.WriteLine($"Insufficient funds: Expense amount ({expenseToSave.Amount:C2}) > Drawer balance ({drawer.CurrentBalance:C2})");
                    MessageBox.Show("Insufficient funds in drawer.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    IsLoading = false;
                    return;
                }

                // Save the expense
                if (expenseToSave.ExpenseId == 0)
                {
                    Debug.WriteLine("Creating new expense...");
                    var savedExpense = await ExecuteDbOperationSafelyAsync(
                        async () => await _expenseService.CreateAsync(expenseToSave),
                        "Creating expense");

                    Debug.WriteLine($"Expense created successfully with ID: {savedExpense.ExpenseId}");

                    // Immediately add to local collection to avoid database round-trip
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Debug.WriteLine("Updating local expenses collection...");
                        // Add to our local collection right away
                        if (Expenses == null)
                        {
                            Debug.WriteLine("Creating new Expenses collection");
                            Expenses = new ObservableCollection<ExpenseDTO>();
                        }

                        Debug.WriteLine($"Adding new expense to collection (now has {Expenses.Count + 1} items)");
                        Expenses.Add(savedExpense);

                        // Close popup after successful save
                        CloseExpensePopup();

                        Debug.WriteLine("Clearing expense form");
                        InitNewExpense();

                        // Re-apply filter to update view
                        Debug.WriteLine("Re-applying filter after adding expense");
                        ApplyFilter();
                    });

                    await ShowSuccessMessage("Expense saved successfully.");
                }
                else
                {
                    Debug.WriteLine($"Updating existing expense ID: {expenseToSave.ExpenseId}");
                    await ExecuteDbOperationSafelyAsync(
                        async () => await _expenseService.UpdateAsync(expenseToSave),
                        "Updating expense");

                    Debug.WriteLine("Expense updated successfully");

                    // Update UI
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Debug.WriteLine("Updating local expenses collection for the updated item");

                        // Find and update the expense in the collection
                        var existingExpense = Expenses?.FirstOrDefault(e => e.ExpenseId == expenseToSave.ExpenseId);
                        if (existingExpense != null)
                        {
                            Debug.WriteLine("Found existing expense in collection, updating it");
                            var index = Expenses.IndexOf(existingExpense);
                            if (index >= 0)
                            {
                                Expenses[index] = expenseToSave;
                            }
                        }

                        // Close popup after successful save
                        CloseExpensePopup();

                        Debug.WriteLine("Clearing expense form");
                        InitNewExpense();

                        // Re-apply filter to update view
                        Debug.WriteLine("Re-applying filter after updating expense");
                        ApplyFilter();
                    });

                    await ShowSuccessMessage("Expense updated successfully.");
                }

                // Reload all data to ensure consistency
                Debug.WriteLine("Reloading all expense data after save operation");
                await Task.Delay(500); // Give the database operation time to complete
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in SaveAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await ShowErrorMessageAsync($"Error saving expense: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                Debug.WriteLine("=== END SaveAsync ===");
            }
        }

        private async Task DeleteAsync(ExpenseDTO? expense)
        {
            Debug.WriteLine("=== BEGIN DeleteAsync ===");
            if (expense == null)
            {
                Debug.WriteLine("No expense selected for deletion");
                return;
            }

            Debug.WriteLine($"Attempting to delete expense ID: {expense.ExpenseId}, Reason: {expense.Reason}");

            try
            {
                if (MessageBox.Show("Are you sure you want to delete this expense?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Debug.WriteLine("User confirmed deletion");
                    IsLoading = true;

                    await ExecuteDbOperationSafelyAsync(
                        async () => await _expenseService.DeleteAsync(expense.ExpenseId),
                        "Deleting expense");

                    Debug.WriteLine("Expense deleted from database");

                    // Remove from local collection immediately
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var existingExpense = Expenses?.FirstOrDefault(e => e.ExpenseId == expense.ExpenseId);
                        if (existingExpense != null)
                        {
                            Debug.WriteLine("Removing expense from local collection");
                            Expenses.Remove(existingExpense);

                            // Close popup after successful delete
                            CloseExpensePopup();

                            // Re-apply filter to update view
                            Debug.WriteLine("Re-applying filter after deletion");
                            ApplyFilter();
                        }
                    });

                    await ShowSuccessMessage("Expense deleted successfully.");

                    // Reload data to ensure consistency
                    Debug.WriteLine("Reloading all expense data after deletion");
                    await Task.Delay(300);
                    await LoadDataAsync();
                }
                else
                {
                    Debug.WriteLine("User cancelled deletion");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in DeleteAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await ShowErrorMessageAsync($"Error deleting expense: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                Debug.WriteLine("=== END DeleteAsync ===");
            }
        }

        private void EditExpense(ExpenseDTO? expense)
        {
            Debug.WriteLine("=== BEGIN EditExpense ===");
            if (expense == null)
            {
                Debug.WriteLine("No expense provided for editing");
                return;
            }

            Debug.WriteLine($"Editing expense ID: {expense.ExpenseId}, Reason: {expense.Reason}, Amount: {expense.Amount}");

            CurrentExpense = new ExpenseDTO
            {
                ExpenseId = expense.ExpenseId,
                Reason = expense.Reason,
                Amount = expense.Amount,
                Date = expense.Date,
                Notes = expense.Notes,
                Category = expense.Category,
                IsRecurring = expense.IsRecurring,
                CreatedAt = expense.CreatedAt,
                UpdatedAt = DateTime.Now
            };

            IsNewExpense = false;
            ShowExpensePopup();

            Debug.WriteLine($"CurrentExpense set for editing, ID: {CurrentExpense.ExpenseId}");
            Debug.WriteLine("=== END EditExpense ===");
        }

        private void ClearForm()
        {
            Debug.WriteLine("=== BEGIN ClearForm ===");

            string defaultCategory = Categories?.Count > 1 ? Categories[1] : Categories?.FirstOrDefault() ?? "Other";
            Debug.WriteLine($"Using default category: {defaultCategory}");

            CurrentExpense = new ExpenseDTO
            {
                Date = DateTime.Today,
                IsRecurring = false,
                Category = defaultCategory
            };

            IsNewExpense = true;
            ShowExpensePopup();

            Debug.WriteLine("Form cleared");
            Debug.WriteLine("=== END ClearForm ===");
        }

        // Initialize a new expense (without showing popup)
        private void InitNewExpense()
        {
            string defaultCategory = Categories?.Count > 1 ? Categories[1] : Categories?.FirstOrDefault() ?? "Other";

            CurrentExpense = new ExpenseDTO
            {
                Date = DateTime.Today,
                IsRecurring = false,
                Category = defaultCategory
            };
        }

        private async Task ShowSuccessMessage(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        protected override void Dispose(bool disposing)
        {
            Debug.WriteLine("=== BEGIN Dispose ===");
            if (disposing)
            {
                Debug.WriteLine("Disposing ExpenseViewModel resources");
                _operationLock?.Dispose();

                // Unsubscribe from events
                if (_eventAggregator != null)
                {
                    Debug.WriteLine("Unsubscribing from events");
                    _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(HandleCategoryChanged);
                    _eventAggregator.Unsubscribe<EntityChangedEvent<ExpenseDTO>>(_expenseChangedHandler);
                }

                // Clear collections
                Debug.WriteLine("Clearing collections");
                Expenses?.Clear();
                Categories?.Clear();
                CategorySummaries?.Clear();

                Debug.WriteLine("ExpenseViewModel disposed");
            }
            base.Dispose(disposing);
            Debug.WriteLine("=== END Dispose ===");
        }

        public class CategorySummary
        {
            public string CategoryName { get; set; } = string.Empty;
            public int Count { get; set; }
            public decimal TotalAmount { get; set; }
            public bool IsTotal { get; set; }
        }
    }
}