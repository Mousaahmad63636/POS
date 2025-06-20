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
        private FlowDirection _flowDirection = FlowDirection.LeftToRight;

        // Pagination properties
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalCount = 0;
        private int _totalPages = 0;
        private bool _hasPreviousPage = false;
        private bool _hasNextPage = false;
        private ObservableCollection<int> _availablePageSizes;

        // Improved concurrency control
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _operationInProgress = false;
        private CancellationTokenSource _loadingCts;
        private int _loadingSequence = 0;
        private readonly object _sequenceLock = new object();
        private bool _skipEventHandling = false; // Flag to temporarily skip event handling

        // Debouncing for events
        private readonly Dictionary<string, DateTime> _lastEventTime = new Dictionary<string, DateTime>();
        private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);
        private int _totalExpenseCount;
        public int TotalExpenseCount
        {
            get => _totalExpenseCount;
            set => SetProperty(ref _totalExpenseCount, value);
        }

        private decimal _totalExpenseAmount;
        public decimal TotalExpenseAmount
        {
            get => _totalExpenseAmount;
            set => SetProperty(ref _totalExpenseAmount, value);
        }

        // Pagination Properties
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    Debug.WriteLine($"CurrentPage changed to: {value}");
                    // Immediately reload data for new page
                    Task.Run(async () =>
                    {
                        await Task.Delay(50); // Small delay to ensure UI updates
                        await LoadPagedDataAsync();
                    });
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
                    Debug.WriteLine($"PageSize changed to: {value}");
                    CurrentPage = 1; // Reset to first page when page size changes

                    // Immediately reload data with new page size
                    Task.Run(async () =>
                    {
                        await Task.Delay(100); // Small delay to ensure UI updates
                        await LoadPagedDataAsync();
                    });
                }
            }
        }

        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        public int TotalPages
        {
            get => _totalPages;
            set => SetProperty(ref _totalPages, value);
        }

        public bool HasPreviousPage
        {
            get => _hasPreviousPage;
            set => SetProperty(ref _hasPreviousPage, value);
        }

        public bool HasNextPage
        {
            get => _hasNextPage;
            set => SetProperty(ref _hasNextPage, value);
        }

        public string PaginationInfo => $"Page {CurrentPage} of {TotalPages} ({TotalCount} total items)";

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
            _loadingCts = new CancellationTokenSource();
            _availablePageSizes = new ObservableCollection<int> { 5, 10, 20, 50, 100 };

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

            // Pagination commands
            FirstPageCommand = new RelayCommand(_ => CurrentPage = 1, _ => HasPreviousPage);
            PreviousPageCommand = new RelayCommand(_ => CurrentPage--, _ => HasPreviousPage);
            NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => HasNextPage);
            LastPageCommand = new RelayCommand(_ => CurrentPage = TotalPages, _ => HasNextPage);
            RefreshCommand = new AsyncRelayCommand(async _ => await RefreshDataAsync());

            Debug.WriteLine("Starting initial data load...");

            // Initialize data with a consolidated, more robust approach
            Task.Run(async () => {
                await Task.Delay(500); // Initial delay to ensure view is ready
                await InitializeDataAsync();
            });

            Debug.WriteLine("ExpenseViewModel initialization complete");
        }

        // Consolidated initialization to prevent overlapping loads
        private async Task InitializeDataAsync()
        {
            Debug.WriteLine("=== BEGIN InitializeDataAsync ===");
            try
            {
                // Get the current loading sequence and create a new token
                int currentSequence;
                CancellationToken token;

                lock (_sequenceLock)
                {
                    _loadingSequence++;
                    currentSequence = _loadingSequence;
                    // Cancel any previous loading operation
                    if (_loadingCts != null)
                    {
                        _loadingCts.Cancel();
                        _loadingCts.Dispose();
                    }
                    _loadingCts = new CancellationTokenSource();
                    token = _loadingCts.Token;
                }

                Debug.WriteLine($"Starting initialization sequence {currentSequence}");

                // First load categories
                bool categoriesLoaded = await LoadCategoriesAsync(token);

                if (token.IsCancellationRequested)
                {
                    Debug.WriteLine($"Initialization sequence {currentSequence} was cancelled after loading categories");
                    return;
                }

                if (categoriesLoaded)
                {
                    // Then load expenses with pagination
                    await LoadPagedDataAsync(token);
                }
                else
                {
                    Debug.WriteLine("Failed to load categories, skipping expense loading");
                }

                Debug.WriteLine($"Initialization sequence {currentSequence} completed");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Initialization was cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InitializeDataAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                Debug.WriteLine("=== END InitializeDataAsync ===");
            }
        }

        // New method for loading paginated data
        private async Task LoadPagedDataAsync(CancellationToken token = default)
        {
            Debug.WriteLine("=== BEGIN LoadPagedDataAsync ===");
            if (IsLoading)
            {
                Debug.WriteLine("Already loading data, skipping this request");
                return;
            }

            try
            {
                IsLoading = true;
                Debug.WriteLine($"Loading page {CurrentPage} with page size {PageSize}");

                // Determine filter parameters
                string? categoryFilter = (string.IsNullOrEmpty(SelectedCategory) || SelectedCategory == "All") ? null : SelectedCategory;
                DateTime? startDateFilter = FilterStartDate;

                var pagedResult = await ExecuteDbOperationSafelyAsync(
                    () => _expenseService.GetPagedAsync(CurrentPage, PageSize, categoryFilter, startDateFilter, null),
                    "Loading paged expenses",
                    token);

                if (token.IsCancellationRequested)
                {
                    Debug.WriteLine("Paged expense loading operation was cancelled");
                    return;
                }

                Debug.WriteLine($"Successfully loaded page {pagedResult.PageNumber} with {pagedResult.Items.Count()} expenses");

                // Update UI on the dispatcher thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;

                    Debug.WriteLine("Updating UI with paged expenses...");
                    Expenses = new ObservableCollection<ExpenseDTO>(pagedResult.Items);

                    // Update pagination properties
                    TotalCount = pagedResult.TotalCount;
                    TotalPages = pagedResult.TotalPages;
                    HasPreviousPage = pagedResult.HasPreviousPage;
                    HasNextPage = pagedResult.HasNextPage;

                    // Update summary counts
                    TotalExpenseCount = pagedResult.Items.Count();
                    TotalExpenseAmount = pagedResult.Items.Sum(e => e.Amount);

                    Debug.WriteLine($"Updated pagination: Page {CurrentPage}/{TotalPages}, Total: {TotalCount}");

                    // Update category summaries for current page
                    UpdateCategorySummaries();

                    // Notify property changes for pagination info
                    OnPropertyChanged(nameof(PaginationInfo));
                });
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("LoadPagedDataAsync was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in LoadPagedDataAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Show error to user on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await ShowErrorMessageAsync($"Error loading data: {ex.Message}");
                });
            }
            finally
            {
                IsLoading = false;
                Debug.WriteLine("=== END LoadPagedDataAsync ===");
            }
        }

        // Public method for manual refresh
        public async Task RefreshDataAsync()
        {
            Debug.WriteLine("=== Manual Refresh Requested ===");
            await LoadPagedDataAsync();
        }

        // Helper method to update category summaries for current page
        private void UpdateCategorySummaries()
        {
            if (Expenses == null || !Expenses.Any())
            {
                CategorySummaries = new ObservableCollection<CategorySummary>();
                return;
            }

            var summaries = Expenses
                .GroupBy(e => e.Category)
                .Select(g => new CategorySummary
                {
                    CategoryName = g.Key,
                    Count = g.Count(),
                    TotalAmount = g.Sum(e => e.Amount)
                })
                .OrderBy(s => s.CategoryName)
                .ToList();

            // Add a total row for current page
            summaries.Add(new CategorySummary
            {
                CategoryName = "Page Total",
                Count = summaries.Sum(s => s.Count),
                TotalAmount = summaries.Sum(s => s.TotalAmount),
                IsTotal = true
            });

            CategorySummaries = new ObservableCollection<CategorySummary>(summaries);
        }

        // Enhanced operation safety with cancellation support
        private async Task<T> ExecuteDbOperationSafelyAsync<T>(Func<Task<T>> operation, string operationName = "Database operation", CancellationToken token = default)
        {
            Debug.WriteLine($"BEGIN: {operationName}");

            // If an operation is already in progress, wait a bit
            int waitCount = 0;
            while (_operationInProgress && !token.IsCancellationRequested)
            {
                waitCount++;
                Debug.WriteLine($"Operation in progress, waiting... (attempt {waitCount})");
                try
                {
                    await Task.Delay(100, token);
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"Waiting for operation lock was cancelled: {operationName}");
                    throw;
                }

                // Safety timeout
                if (waitCount > 20) // 2 seconds max wait
                {
                    Debug.WriteLine("TIMEOUT waiting for operation lock, proceeding anyway");
                    break;
                }
            }

            if (token.IsCancellationRequested)
            {
                Debug.WriteLine($"Operation was cancelled before acquiring lock: {operationName}");
                throw new OperationCanceledException(token);
            }

            Debug.WriteLine($"Acquiring operation lock for: {operationName}");
            await _operationLock.WaitAsync(token);
            _operationInProgress = true;

            try
            {
                Debug.WriteLine($"Executing operation: {operationName}");
                await Task.Delay(50, token);
                var result = await operation();
                Debug.WriteLine($"Operation completed successfully: {operationName}");
                return result;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Operation was cancelled during execution: {operationName}");
                throw;
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

        // Overload for void operations with cancellation support
        private async Task ExecuteDbOperationSafelyAsync(Func<Task> operation, string operationName = "Database operation", CancellationToken token = default)
        {
            await ExecuteDbOperationSafelyAsync<bool>(async () =>
            {
                await operation();
                return true;
            }, operationName, token);
        }

        // Properties
        public ObservableCollection<int> AvailablePageSizes
        {
            get => _availablePageSizes;
            set => SetProperty(ref _availablePageSizes, value);
        }

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

        public FlowDirection FlowDirection
        {
            get => _flowDirection;
            set => SetProperty(ref _flowDirection, value);
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

        // Pagination commands
        public ICommand FirstPageCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand RefreshCommand { get; }

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

        // Debounced event handler for category changes
        private async void HandleCategoryChanged(EntityChangedEvent<CategoryDTO> evt)
        {
            Debug.WriteLine($"CategoryChanged event received: {evt.Action} - {evt.Entity.Name}");
            try
            {
                // Implement debouncing
                string eventKey = "CategoryChanged";
                if (!ShouldProcessEvent(eventKey))
                {
                    Debug.WriteLine($"Debouncing CategoryChanged event for {evt.Entity.Name}");
                    return;
                }

                // Cancel any previous loading operation
                CancellationToken token;
                lock (_sequenceLock)
                {
                    _loadingSequence++;
                    if (_loadingCts != null)
                    {
                        _loadingCts.Cancel();
                        _loadingCts.Dispose();
                    }
                    _loadingCts = new CancellationTokenSource();
                    token = _loadingCts.Token;
                }

                await LoadCategoriesAsync(token);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Category loading was cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling category change: {ex.Message}");
            }
        }

        // Debounced event handler for expense changes
        private async void HandleExpenseChanged(EntityChangedEvent<ExpenseDTO> evt)
        {
            Debug.WriteLine($"ExpenseChanged event received: {evt.Action} - ID:{evt.Entity.ExpenseId}, Reason:{evt.Entity.Reason}");

            // Skip event handling if we're in the middle of a manual operation
            if (_skipEventHandling)
            {
                Debug.WriteLine("Skipping event handling - manual operation in progress");
                return;
            }

            try
            {
                // Implement debouncing
                string eventKey = $"ExpenseChanged_{evt.Action}";
                if (!ShouldProcessEvent(eventKey))
                {
                    Debug.WriteLine($"Debouncing ExpenseChanged event for {evt.Entity.ExpenseId}");
                    return;
                }

                Debug.WriteLine("Processing expense change by reloading paged data...");

                // Use a simple reload without complex cancellation handling
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300); // Wait a bit for any ongoing operations
                    await LoadPagedDataAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling expense change: {ex.Message}");
            }
        }

        // Helper method for event debouncing
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

        // Data loading methods with cancellation support
        protected override async Task LoadDataAsync()
        {
            await LoadPagedDataAsync(CancellationToken.None);
        }

        private async Task<bool> LoadCategoriesAsync(CancellationToken token = default)
        {
            Debug.WriteLine("=== BEGIN LoadCategoriesAsync ===");
            try
            {
                IsLoading = true;
                Debug.WriteLine("Loading expense categories...");

                var expenseCategories = await ExecuteDbOperationSafelyAsync(
                    () => _categoryService.GetExpenseCategoriesAsync(),
                    "Loading expense categories",
                    token);

                if (token.IsCancellationRequested)
                {
                    Debug.WriteLine("Category loading operation was cancelled");
                    return false;
                }

                Debug.WriteLine($"Successfully loaded {expenseCategories.Count()} expense categories");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;

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
                });

                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("LoadCategoriesAsync was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in LoadCategoriesAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    IsLoading = false;
                }
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
            // Reset to first page when filter changes
            CurrentPage = 1;

            // Immediately reload data with new filters
            Task.Run(async () =>
            {
                await Task.Delay(100); // Small delay to ensure UI updates
                await LoadPagedDataAsync();
            });

            Debug.WriteLine("Filter applied, reset to page 1 and triggered reload");
            Debug.WriteLine("=== END ApplyFilter ===");
        }

        private async Task SaveAsync()
        {
            Debug.WriteLine("=== BEGIN SaveAsync ===");
            try
            {
                // Temporarily disable event handling to avoid conflicts
                _skipEventHandling = true;

                // Input validation - no DB access, so outside the safe execution block
                if (CurrentExpense == null)
                {
                    Debug.WriteLine("CurrentExpense is null, cannot save");
                    return;
                }

                Debug.WriteLine($"Validating expense - Reason: '{CurrentExpense.Reason}', Amount: {CurrentExpense.Amount}");

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

                Debug.WriteLine($"Created expense clone for saving: ID: {expenseToSave.ExpenseId}");

                // Start loading UI indicator
                IsLoading = true;

                // Create a new cancellation token for this operation
                CancellationToken token;
                lock (_sequenceLock)
                {
                    _loadingSequence++;
                    if (_loadingCts != null)
                    {
                        _loadingCts.Cancel();
                        _loadingCts.Dispose();
                    }
                    _loadingCts = new CancellationTokenSource();
                    token = _loadingCts.Token;
                }

                // Pre-check drawer balance
                Debug.WriteLine("Checking drawer balance");
                var drawer = await ExecuteDbOperationSafelyAsync(
                    () => _drawerService.GetCurrentDrawerAsync(),
                    "Checking drawer balance",
                    token);

                if (token.IsCancellationRequested)
                {
                    Debug.WriteLine("Save operation was cancelled");
                    return;
                }

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
                    Debug.WriteLine($"Insufficient funds: Expense amount > Drawer balance");
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
                        "Creating expense",
                        token);

                    if (token.IsCancellationRequested)
                    {
                        Debug.WriteLine("Save operation was cancelled after create");
                        return;
                    }

                    Debug.WriteLine($"Expense created successfully with ID: {savedExpense.ExpenseId}");

                    // Close popup and clear form
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        CloseExpensePopup();
                        InitNewExpense();
                    });

                    await ShowSuccessMessage("Expense saved successfully.");

                    // Refresh current page - use a separate task to avoid cancellation issues
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500); // Wait for database to settle
                        await LoadPagedDataAsync();
                    });
                }
                else
                {
                    Debug.WriteLine($"Updating existing expense ID: {expenseToSave.ExpenseId}");
                    await ExecuteDbOperationSafelyAsync(
                        async () => await _expenseService.UpdateAsync(expenseToSave),
                        "Updating expense",
                        token);

                    if (token.IsCancellationRequested)
                    {
                        Debug.WriteLine("Save operation was cancelled after update");
                        return;
                    }

                    Debug.WriteLine("Expense updated successfully");

                    // Close popup and clear form
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        CloseExpensePopup();
                        InitNewExpense();
                    });

                    await ShowSuccessMessage("Expense updated successfully.");

                    // Refresh current page - use a separate task to avoid cancellation issues
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500); // Wait for database to settle
                        await LoadPagedDataAsync();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SaveAsync was cancelled");
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
                // Re-enable event handling
                _skipEventHandling = false;
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

            Debug.WriteLine($"Attempting to delete expense ID: {expense.ExpenseId}");

            try
            {
                // Temporarily disable event handling to avoid conflicts
                _skipEventHandling = true;

                if (MessageBox.Show("Are you sure you want to delete this expense?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Debug.WriteLine("User confirmed deletion");
                    IsLoading = true;

                    // Create a new cancellation token for this operation
                    CancellationToken token;
                    lock (_sequenceLock)
                    {
                        _loadingSequence++;
                        if (_loadingCts != null)
                        {
                            _loadingCts.Cancel();
                            _loadingCts.Dispose();
                        }
                        _loadingCts = new CancellationTokenSource();
                        token = _loadingCts.Token;
                    }

                    await ExecuteDbOperationSafelyAsync(
                        async () => await _expenseService.DeleteAsync(expense.ExpenseId),
                        "Deleting expense",
                        token);

                    if (token.IsCancellationRequested)
                    {
                        Debug.WriteLine("Delete operation was cancelled");
                        return;
                    }

                    Debug.WriteLine("Expense deleted from database");

                    // Close popup if open
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        CloseExpensePopup();
                    });

                    await ShowSuccessMessage("Expense deleted successfully.");

                    // Refresh current page - use a separate task to avoid cancellation issues
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500); // Wait for database to settle
                        await LoadPagedDataAsync();
                    });
                }
                else
                {
                    Debug.WriteLine("User cancelled deletion");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("DeleteAsync was cancelled");
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
                // Re-enable event handling
                _skipEventHandling = false;
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

            Debug.WriteLine($"Editing expense ID: {expense.ExpenseId}");

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

                // Cancel any pending operations
                lock (_sequenceLock)
                {
                    if (_loadingCts != null)
                    {
                        _loadingCts.Cancel();
                        _loadingCts.Dispose();
                        _loadingCts = null;
                    }
                }

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