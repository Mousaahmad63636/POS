using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.ViewModels.Expense
{
    public class ExpenseViewModel : ViewModelBase
    {
        private readonly IExpenseService _expenseService;
        private readonly IDrawerService _drawerService;
        private readonly ICategoryService _categoryService;

        private ObservableCollection<ExpenseDTO> _expenses;
        private ObservableCollection<ExpenseDTO> _filteredExpenses;
        private ExpenseDTO? _selectedExpense;
        private ExpenseDTO _currentExpense;
        private ObservableCollection<string> _categories;
        private string _selectedCategory = "All";
        private DateTime _filterStartDate;
        private DateTime _filterEndDate;
        private string _searchText = string.Empty;
        private ObservableCollection<CategorySummary> _categorySummaries;
        private bool _isLoading;
        private bool _isExpensePopupOpen;
        private bool _isNewExpense;
        private FlowDirection _flowDirection = FlowDirection.LeftToRight;

        // Quick filter options
        private string _selectedQuickFilter = "This Month";
        private ObservableCollection<string> _quickFilters;

        // Summary statistics
        private decimal _totalExpenseAmount;
        private int _totalExpenseCount;
        private decimal _averageExpenseAmount;
        private decimal _todayExpenses;
        private decimal _thisWeekExpenses;
        private decimal _thisMonthExpenses;
        private string _topCategory = string.Empty;
        private decimal _topCategoryAmount;

        // Sorting
        private string _sortBy = "Date";
        private bool _sortDescending = true;
        private ObservableCollection<string> _sortOptions;

        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _eventHandlingLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, DateTime> _lastEventTime = new Dictionary<string, DateTime>();
        private readonly Dictionary<int, DateTime> _operationTimestamps = new Dictionary<int, DateTime>();
        private readonly HashSet<int> _pendingOperations = new HashSet<int>();
        private readonly Dictionary<string, CancellationTokenSource> _operationCancellations = new Dictionary<string, CancellationTokenSource>();

        private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(1000);
        private volatile bool _disposed = false;
        private int _loadingSequence = 0;

        public ExpenseViewModel(
            IExpenseService expenseService,
            IDrawerService drawerService,
            ICategoryService categoryService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _expenseService = expenseService ?? throw new ArgumentNullException(nameof(expenseService));
            _drawerService = drawerService ?? throw new ArgumentNullException(nameof(drawerService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));

            InitializeCollections();
            InitializeDefaultValues();
            InitializeCommands();
            InitializeAsync();
        }

        private void InitializeCollections()
        {
            _expenses = new ObservableCollection<ExpenseDTO>();
            _filteredExpenses = new ObservableCollection<ExpenseDTO>();
            _categorySummaries = new ObservableCollection<CategorySummary>();
            _categories = new ObservableCollection<string>();
            _quickFilters = new ObservableCollection<string>
            {
                "Today", "Yesterday", "This Week", "Last Week",
                "This Month", "Last Month", "This Quarter", "This Year", "Custom"
            };
            _sortOptions = new ObservableCollection<string>
            {
                "Date", "Amount", "Category", "Reason"
            };
        }

        private void InitializeDefaultValues()
        {
            var now = DateTime.Now;
            _filterStartDate = new DateTime(now.Year, now.Month, 1);
            _filterEndDate = now.Date.AddDays(1).AddSeconds(-1);
            _currentExpense = new ExpenseDTO { Date = DateTime.Today };
        }

        private void InitializeCommands()
        {
            DeleteCommand = new AsyncRelayCommand(async param => await ExecuteWithIsolationAsync(() => DeleteAsync(param as ExpenseDTO)));
            EditCommand = new RelayCommand(param => EditExpense(param as ExpenseDTO));
            ClearCommand = new RelayCommand(_ => ClearForm());
            SaveCommand = new AsyncRelayCommand(async _ => await ExecuteWithIsolationAsync(SaveAsync));
            ApplyFilterCommand = new RelayCommand(_ => ApplyFilter());
            QuickFilterCommand = new RelayCommand(param => ApplyQuickFilter(param?.ToString()));
            SearchCommand = new RelayCommand(_ => ApplyFilter());
            ExportCommand = new AsyncRelayCommand(async _ => await ExportExpensesAsync());
            RefreshCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
        }

        #region Properties

        public ObservableCollection<ExpenseDTO> Expenses
        {
            get => _expenses;
            set => SetProperty(ref _expenses, value);
        }

        public ObservableCollection<ExpenseDTO> FilteredExpenses
        {
            get => _filteredExpenses;
            set => SetProperty(ref _filteredExpenses, value);
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
                if (SetProperty(ref _selectedCategory, value))
                    ApplyFilter();
            }
        }

        public DateTime FilterStartDate
        {
            get => _filterStartDate;
            set
            {
                if (SetProperty(ref _filterStartDate, value))
                {
                    if (_selectedQuickFilter == "Custom")
                        ApplyFilter();
                }
            }
        }

        public DateTime FilterEndDate
        {
            get => _filterEndDate;
            set
            {
                if (SetProperty(ref _filterEndDate, value))
                {
                    if (_selectedQuickFilter == "Custom")
                        ApplyFilter();
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter();
            }
        }

        public string SelectedQuickFilter
        {
            get => _selectedQuickFilter;
            set
            {
                if (SetProperty(ref _selectedQuickFilter, value))
                    ApplyQuickFilter(value);
            }
        }

        public ObservableCollection<string> QuickFilters
        {
            get => _quickFilters;
            set => SetProperty(ref _quickFilters, value);
        }

        public string SortBy
        {
            get => _sortBy;
            set
            {
                if (SetProperty(ref _sortBy, value))
                    ApplyFilter();
            }
        }

        public bool SortDescending
        {
            get => _sortDescending;
            set
            {
                if (SetProperty(ref _sortDescending, value))
                    ApplyFilter();
            }
        }

        public ObservableCollection<string> SortOptions
        {
            get => _sortOptions;
            set => SetProperty(ref _sortOptions, value);
        }

        public ObservableCollection<CategorySummary> CategorySummaries
        {
            get => _categorySummaries;
            set => SetProperty(ref _categorySummaries, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
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

        public FlowDirection FlowDirection
        {
            get => _flowDirection;
            set => SetProperty(ref _flowDirection, value);
        }

        // Summary Statistics
        public decimal TotalExpenseAmount
        {
            get => _totalExpenseAmount;
            set => SetProperty(ref _totalExpenseAmount, value);
        }

        public int TotalExpenseCount
        {
            get => _totalExpenseCount;
            set => SetProperty(ref _totalExpenseCount, value);
        }

        public decimal AverageExpenseAmount
        {
            get => _averageExpenseAmount;
            set => SetProperty(ref _averageExpenseAmount, value);
        }

        public decimal TodayExpenses
        {
            get => _todayExpenses;
            set => SetProperty(ref _todayExpenses, value);
        }

        public decimal ThisWeekExpenses
        {
            get => _thisWeekExpenses;
            set => SetProperty(ref _thisWeekExpenses, value);
        }

        public decimal ThisMonthExpenses
        {
            get => _thisMonthExpenses;
            set => SetProperty(ref _thisMonthExpenses, value);
        }

        public string TopCategory
        {
            get => _topCategory;
            set => SetProperty(ref _topCategory, value);
        }

        public decimal TopCategoryAmount
        {
            get => _topCategoryAmount;
            set => SetProperty(ref _topCategoryAmount, value);
        }

        public bool IsCustomDateRange => _selectedQuickFilter == "Custom";

        #endregion

        #region Commands

        public ICommand SaveCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand ClearCommand { get; private set; }
        public ICommand ApplyFilterCommand { get; private set; }
        public ICommand QuickFilterCommand { get; private set; }
        public ICommand SearchCommand { get; private set; }
        public ICommand ExportCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }

        #endregion

        #region Event Handling

        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();
            _eventAggregator.Subscribe<EntityChangedEvent<CategoryDTO>>(HandleCategoryChangedWithIsolation);
            _eventAggregator.Subscribe<EntityChangedEvent<ExpenseDTO>>(HandleExpenseChangedWithIsolation);
        }

        protected override void UnsubscribeFromEvents()
        {
            base.UnsubscribeFromEvents();
            _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(HandleCategoryChangedWithIsolation);
            _eventAggregator.Unsubscribe<EntityChangedEvent<ExpenseDTO>>(HandleExpenseChangedWithIsolation);
        }

        private async void HandleCategoryChangedWithIsolation(EntityChangedEvent<CategoryDTO> evt)
        {
            if (_disposed || !ShouldProcessEvent($"CategoryChanged_{evt.Action}_{evt.Entity.CategoryId}"))
                return;

            if (!await _eventHandlingLock.WaitAsync(50))
                return;

            try
            {
                await Task.Delay(500);
                await LoadCategoriesAsync();
            }
            finally
            {
                _eventHandlingLock.Release();
            }
        }

        private async void HandleExpenseChangedWithIsolation(EntityChangedEvent<ExpenseDTO> evt)
        {
            if (_disposed || !ShouldProcessEvent($"ExpenseChanged_{evt.Action}_{evt.Entity.ExpenseId}"))
                return;

            if (!await _eventHandlingLock.WaitAsync(50))
                return;

            try
            {
                await Task.Delay(800);
                await LoadDataAsync();
            }
            finally
            {
                _eventHandlingLock.Release();
            }
        }

        private bool ShouldProcessEvent(string eventKey)
        {
            DateTime now = DateTime.Now;
            if (_lastEventTime.TryGetValue(eventKey, out DateTime lastTime))
            {
                if (now - lastTime < _debounceDelay)
                    return false;
            }

            _lastEventTime[eventKey] = now;
            return true;
        }

        #endregion

        #region Data Loading

        private async void InitializeAsync()
        {
            await Task.Run(async () =>
            {
                await Task.Delay(300);
                await LoadCategoriesAsync();
                await Task.Delay(200);
                await LoadDataAsync();
            });
        }

        protected override async Task LoadDataAsync()
        {
            if (_disposed || IsLoading)
                return;

            var currentSequence = Interlocked.Increment(ref _loadingSequence);

            try
            {
                IsLoading = true;

                var expenses = await _expenseService.GetAllAsync();

                if (currentSequence != _loadingSequence)
                    return;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || currentSequence != _loadingSequence)
                        return;

                    Expenses = new ObservableCollection<ExpenseDTO>(expenses);
                    ApplyFilter();
                    CalculateStatistics();
                });
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error loading expenses", ex);
            }
            finally
            {
                if (currentSequence == _loadingSequence)
                    IsLoading = false;
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var expenseCategories = await _categoryService.GetExpenseCategoriesAsync();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;

                    Categories.Clear();
                    Categories.Add("All");

                    foreach (var category in expenseCategories.Where(c => c.IsActive))
                        Categories.Add(category.Name);

                    if (!Categories.Contains("Other"))
                        Categories.Add("Other");

                    if (CurrentExpense?.Category == null && Categories.Count > 1)
                        CurrentExpense.Category = Categories[1];
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading categories: {ex.Message}");
            }
        }

        #endregion

        #region Filtering and Sorting

        private void ApplyQuickFilter(string? filter)
        {
            if (string.IsNullOrEmpty(filter)) return;

            var now = DateTime.Now;
            var today = now.Date;

            switch (filter)
            {
                case "Today":
                    FilterStartDate = today;
                    FilterEndDate = today.AddDays(1).AddSeconds(-1);
                    break;
                case "Yesterday":
                    FilterStartDate = today.AddDays(-1);
                    FilterEndDate = today.AddSeconds(-1);
                    break;
                case "This Week":
                    var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                    FilterStartDate = startOfWeek;
                    FilterEndDate = startOfWeek.AddDays(7).AddSeconds(-1);
                    break;
                case "Last Week":
                    var lastWeekStart = today.AddDays(-(int)today.DayOfWeek - 7);
                    FilterStartDate = lastWeekStart;
                    FilterEndDate = lastWeekStart.AddDays(7).AddSeconds(-1);
                    break;
                case "This Month":
                    FilterStartDate = new DateTime(now.Year, now.Month, 1);
                    FilterEndDate = FilterStartDate.AddMonths(1).AddSeconds(-1);
                    break;
                case "Last Month":
                    var lastMonth = now.AddMonths(-1);
                    FilterStartDate = new DateTime(lastMonth.Year, lastMonth.Month, 1);
                    FilterEndDate = FilterStartDate.AddMonths(1).AddSeconds(-1);
                    break;
                case "This Quarter":
                    var quarter = (now.Month - 1) / 3 + 1;
                    FilterStartDate = new DateTime(now.Year, (quarter - 1) * 3 + 1, 1);
                    FilterEndDate = FilterStartDate.AddMonths(3).AddSeconds(-1);
                    break;
                case "This Year":
                    FilterStartDate = new DateTime(now.Year, 1, 1);
                    FilterEndDate = new DateTime(now.Year + 1, 1, 1).AddSeconds(-1);
                    break;
                case "Custom":
                    // Don't change dates for custom filter
                    break;
            }

            if (filter != "Custom")
                ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_disposed || Expenses == null)
            {
                FilteredExpenses = new ObservableCollection<ExpenseDTO>();
                return;
            }

            var filtered = Expenses.AsEnumerable();

            // Date range filter
            filtered = filtered.Where(e => e.Date >= FilterStartDate && e.Date <= FilterEndDate);

            // Category filter
            if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All")
                filtered = filtered.Where(e => e.Category == SelectedCategory);

            // Search filter
            if (!string.IsNullOrEmpty(SearchText))
            {
                var searchLower = SearchText.ToLower();
                filtered = filtered.Where(e =>
                    e.Reason.ToLower().Contains(searchLower) ||
                    e.Category.ToLower().Contains(searchLower) ||
                    (e.Notes?.ToLower().Contains(searchLower) ?? false));
            }

            // Apply sorting
            filtered = SortBy switch
            {
                "Date" => SortDescending ? filtered.OrderByDescending(e => e.Date) : filtered.OrderBy(e => e.Date),
                "Amount" => SortDescending ? filtered.OrderByDescending(e => e.Amount) : filtered.OrderBy(e => e.Amount),
                "Category" => SortDescending ? filtered.OrderByDescending(e => e.Category) : filtered.OrderBy(e => e.Category),
                "Reason" => SortDescending ? filtered.OrderByDescending(e => e.Reason) : filtered.OrderBy(e => e.Reason),
                _ => filtered.OrderByDescending(e => e.Date)
            };

            FilteredExpenses = new ObservableCollection<ExpenseDTO>(filtered);
            UpdateCategorySummaries(filtered);
            CalculateFilteredStatistics(filtered);
        }

        private void UpdateCategorySummaries(IEnumerable<ExpenseDTO> filteredExpenses)
        {
            var summaries = filteredExpenses
                .GroupBy(e => e.Category)
                .Select(g => new CategorySummary
                {
                    CategoryName = g.Key,
                    Count = g.Count(),
                    TotalAmount = g.Sum(e => e.Amount),
                    AverageAmount = g.Average(e => e.Amount),
                    Percentage = 0 // Will be calculated below
                })
                .OrderByDescending(s => s.TotalAmount)
                .ToList();

            var totalAmount = summaries.Sum(s => s.TotalAmount);
            foreach (var summary in summaries)
            {
                summary.Percentage = totalAmount > 0 ? (summary.TotalAmount / totalAmount) * 100 : 0;
            }

            CategorySummaries = new ObservableCollection<CategorySummary>(summaries);
        }

        private void CalculateFilteredStatistics(IEnumerable<ExpenseDTO> filteredExpenses)
        {
            var expenseList = filteredExpenses.ToList();
            TotalExpenseCount = expenseList.Count;
            TotalExpenseAmount = expenseList.Sum(e => e.Amount);
            AverageExpenseAmount = expenseList.Any() ? expenseList.Average(e => e.Amount) : 0;

            if (expenseList.Any())
            {
                var topCategoryGroup = expenseList.GroupBy(e => e.Category)
                    .OrderByDescending(g => g.Sum(e => e.Amount))
                    .FirstOrDefault();

                TopCategory = topCategoryGroup?.Key ?? "";
                TopCategoryAmount = topCategoryGroup?.Sum(e => e.Amount) ?? 0;
            }
            else
            {
                TopCategory = "";
                TopCategoryAmount = 0;
            }
        }

        #endregion

        #region Statistics

        private void CalculateStatistics()
        {
            if (Expenses == null || !Expenses.Any()) return;

            var now = DateTime.Now;
            var today = now.Date;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            TodayExpenses = Expenses.Where(e => e.Date.Date == today).Sum(e => e.Amount);
            ThisWeekExpenses = Expenses.Where(e => e.Date >= startOfWeek && e.Date < startOfWeek.AddDays(7)).Sum(e => e.Amount);
            ThisMonthExpenses = Expenses.Where(e => e.Date >= startOfMonth && e.Date < startOfMonth.AddMonths(1)).Sum(e => e.Amount);
        }

        #endregion

        #region CRUD Operations

        private async Task ExecuteWithIsolationAsync(Func<Task> operation)
        {
            if (_disposed) return;

            var operationId = Guid.NewGuid().ToString();
            var cancellationSource = new CancellationTokenSource();

            lock (_operationCancellations)
            {
                _operationCancellations[operationId] = cancellationSource;
            }

            if (!await _operationLock.WaitAsync(100))
                return;

            try
            {
                if (cancellationSource.Token.IsCancellationRequested)
                    return;

                await operation();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Operation error", ex);
            }
            finally
            {
                _operationLock.Release();
                lock (_operationCancellations)
                {
                    _operationCancellations.Remove(operationId);
                    cancellationSource.Dispose();
                }
            }
        }

        public void ShowExpensePopup()
        {
            IsExpensePopupOpen = true;
        }

        public void CloseExpensePopup()
        {
            IsExpensePopupOpen = false;
        }

        private async Task SaveAsync()
        {
            if (_disposed || CurrentExpense == null) return;

            if (string.IsNullOrWhiteSpace(CurrentExpense.Reason))
            {
                MessageBox.Show("Please enter a reason for the expense.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CurrentExpense.Amount <= 0)
            {
                MessageBox.Show("Please enter a valid amount.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var operationId = CurrentExpense.ExpenseId;
            if (_pendingOperations.Contains(operationId))
                return;

            _pendingOperations.Add(operationId);
            _operationTimestamps[operationId] = DateTime.Now;

            try
            {
                IsLoading = true;
                var expenseToSave = CreateExpenseClone(CurrentExpense);

                if (expenseToSave.ExpenseId == 0)
                {
                    var savedExpense = await _expenseService.CreateAsync(expenseToSave);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (_disposed) return;
                        CloseExpensePopup();
                        InitNewExpense();
                        Expenses?.Insert(0, savedExpense);
                        ApplyFilter();
                        CalculateStatistics();
                    });
                    await ShowSuccessMessage("Expense saved successfully.");
                }
                else
                {
                    await _expenseService.UpdateAsync(expenseToSave);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (_disposed) return;
                        var existingExpense = Expenses?.FirstOrDefault(e => e.ExpenseId == expenseToSave.ExpenseId);
                        if (existingExpense != null)
                        {
                            var index = Expenses.IndexOf(existingExpense);
                            if (index >= 0) Expenses[index] = expenseToSave;
                        }
                        CloseExpensePopup();
                        InitNewExpense();
                        ApplyFilter();
                        CalculateStatistics();
                    });
                    await ShowSuccessMessage("Expense updated successfully.");
                }
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error saving expense", ex);
            }
            finally
            {
                _pendingOperations.Remove(operationId);
                _operationTimestamps.Remove(operationId);
                IsLoading = false;
            }
        }

        private async Task DeleteAsync(ExpenseDTO? expense)
        {
            if (_disposed || expense == null) return;

            var operationId = expense.ExpenseId;
            if (_pendingOperations.Contains(operationId)) return;

            if (MessageBox.Show("Are you sure you want to delete this expense?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            _pendingOperations.Add(operationId);
            _operationTimestamps[operationId] = DateTime.Now;

            try
            {
                IsLoading = true;
                await _expenseService.DeleteAsync(expense.ExpenseId);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;
                    var existingExpense = Expenses?.FirstOrDefault(e => e.ExpenseId == expense.ExpenseId);
                    if (existingExpense != null)
                    {
                        Expenses.Remove(existingExpense);
                        CloseExpensePopup();
                        ApplyFilter();
                        CalculateStatistics();
                    }
                });

                await ShowSuccessMessage("Expense deleted successfully.");
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error deleting expense", ex);
            }
            finally
            {
                _pendingOperations.Remove(operationId);
                _operationTimestamps.Remove(operationId);
                IsLoading = false;
            }
        }

        private void EditExpense(ExpenseDTO? expense)
        {
            if (_disposed || expense == null) return;
            CurrentExpense = CreateExpenseClone(expense);
            IsNewExpense = false;
            ShowExpensePopup();
        }

        private void ClearForm()
        {
            if (_disposed) return;
            InitNewExpense();
            IsNewExpense = true;
            ShowExpensePopup();
        }

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

        private ExpenseDTO CreateExpenseClone(ExpenseDTO source)
        {
            return new ExpenseDTO
            {
                ExpenseId = source.ExpenseId,
                Reason = source.Reason,
                Amount = source.Amount,
                Date = source.Date,
                Notes = source.Notes,
                Category = source.Category,
                IsRecurring = source.IsRecurring,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.ExpenseId != 0 ? DateTime.Now : null
            };
        }

        #endregion

        #region Export

        private async Task ExportExpensesAsync()
        {
            try
            {
                IsLoading = true;

                // Here you would implement export functionality
                // For now, just show a placeholder message
                MessageBox.Show("Export functionality will be implemented based on your requirements.",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error exporting expenses", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Helper Methods

        private async Task ShowSuccessMessage(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private async Task HandleExceptionAsync(string context, Exception ex)
        {
            Debug.WriteLine($"{context}: {ex.Message}");

            string userMessage = ex.Message.Contains("Operation already in progress") ||
                               ex.Message.Contains("second operation") ||
                               ex.Message.Contains("already being tracked")
                ? "Another operation is in progress. Please wait and try again."
                : ex.Message.Contains("Insufficient funds")
                ? ex.Message
                : ex.Message.Contains("not found")
                ? "The requested item was not found. Please refresh and try again."
                : $"An error occurred: {ex.Message}";

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(userMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        #endregion

        #region Disposal

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                lock (_operationCancellations)
                {
                    foreach (var cancellation in _operationCancellations.Values)
                    {
                        cancellation.Cancel();
                        cancellation.Dispose();
                    }
                    _operationCancellations.Clear();
                }

                _operationLock?.Dispose();
                _eventHandlingLock?.Dispose();

                if (_eventAggregator != null)
                {
                    _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(HandleCategoryChangedWithIsolation);
                    _eventAggregator.Unsubscribe<EntityChangedEvent<ExpenseDTO>>(HandleExpenseChangedWithIsolation);
                }

                Expenses?.Clear();
                FilteredExpenses?.Clear();
                Categories?.Clear();
                CategorySummaries?.Clear();
            }

            base.Dispose(disposing);
        }

        #endregion

        #region Nested Classes

        public class CategorySummary
        {
            public string CategoryName { get; set; } = string.Empty;
            public int Count { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal AverageAmount { get; set; }
            public decimal Percentage { get; set; }
            public bool IsTotal { get; set; }
        }

        #endregion
    }
}