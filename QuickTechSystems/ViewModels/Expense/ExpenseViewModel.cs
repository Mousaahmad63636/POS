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

namespace QuickTechSystems.ViewModels.Expense
{
    public class ExpenseViewModel : ViewModelBase
    {
        private readonly IExpenseService _expenseService;
        private readonly IDrawerService _drawerService;
        private readonly ICategoryService _categoryService;

        private ObservableCollection<ExpenseDTO> _expenses;
        private ExpenseDTO? _selectedExpense;
        private ExpenseDTO _currentExpense;
        private ObservableCollection<string> _categories;
        private string _selectedCategory;
        private DateTime _filterStartDate = DateTime.Today;
        private ObservableCollection<CategorySummary> _categorySummaries;
        private bool _isLoading;
        private bool _isExpensePopupOpen;
        private bool _isNewExpense;
        private FlowDirection _flowDirection = FlowDirection.LeftToRight;

        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _eventHandlingLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, DateTime> _lastEventTime = new Dictionary<string, DateTime>();
        private readonly Dictionary<int, DateTime> _operationTimestamps = new Dictionary<int, DateTime>();
        private readonly HashSet<int> _pendingOperations = new HashSet<int>();
        private readonly Queue<Func<Task>> _operationQueue = new Queue<Func<Task>>();
        private readonly Dictionary<string, CancellationTokenSource> _operationCancellations = new Dictionary<string, CancellationTokenSource>();

        private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(1000);
        private readonly TimeSpan _operationCooldown = TimeSpan.FromMilliseconds(2000);
        private volatile bool _disposed = false;
        private int _loadingSequence = 0;

        private int _totalExpenseCount;
        private decimal _totalExpenseAmount;

        public int TotalExpenseCount
        {
            get => _totalExpenseCount;
            set => SetProperty(ref _totalExpenseCount, value);
        }

        public decimal TotalExpenseAmount
        {
            get => _totalExpenseAmount;
            set => SetProperty(ref _totalExpenseAmount, value);
        }

        public ExpenseViewModel(
          IExpenseService expenseService,
          IDrawerService drawerService,
          ICategoryService categoryService,
          IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _expenseService = expenseService ?? throw new ArgumentNullException(nameof(expenseService));
            _drawerService = drawerService ?? throw new ArgumentNullException(nameof(drawerService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));

            _expenses = new ObservableCollection<ExpenseDTO>();
            _currentExpense = new ExpenseDTO { Date = DateTime.Today };
            _categorySummaries = new ObservableCollection<CategorySummary>();
            _categories = new ObservableCollection<string>();

            _filterStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            InitializeCommands();
            InitializeAsync();
        }

        private void InitializeCommands()
        {
            DeleteCommand = new AsyncRelayCommand(async param => await ExecuteWithIsolationAsync(() => DeleteAsync(param as ExpenseDTO)));
            EditCommand = new RelayCommand(param => EditExpense(param as ExpenseDTO));
            ClearCommand = new RelayCommand(_ => ClearForm());
            SaveCommand = new AsyncRelayCommand(async _ => await ExecuteWithIsolationAsync(SaveAsync));
            ApplyFilterCommand = new RelayCommand(_ => ApplyFilter());
        }

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
            {
                return;
            }

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
                {
                    ApplyFilter();
                }
            }
        }

        public ObservableCollection<ExpenseDTO> Expenses
        {
            get => _expenses;
            set => SetProperty(ref _expenses, value);
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
            set => SetProperty(ref _isLoading, value);
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
                if (SetProperty(ref _filterStartDate, value))
                {
                    ApplyFilter();
                }
            }
        }

        public ObservableCollection<CategorySummary> CategorySummaries
        {
            get => _categorySummaries;
            set => SetProperty(ref _categorySummaries, value);
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

        public ICommand SaveCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand ClearCommand { get; private set; }
        public ICommand ApplyFilterCommand { get; private set; }

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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling category change: {ex.Message}");
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling expense change: {ex.Message}");
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

                var expenses = await _expenseService.GetAllAsync();

                if (currentSequence != _loadingSequence)
                    return;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || currentSequence != _loadingSequence)
                        return;

                    Expenses = new ObservableCollection<ExpenseDTO>(
                        expenses.OrderByDescending(e => e.Date));

                    ApplyFilter();
                });
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error loading expenses", ex);
            }
            finally
            {
                if (currentSequence == _loadingSequence)
                {
                    IsLoading = false;
                }
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var expenseCategories = await _categoryService.GetExpenseCategoriesAsync();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed)
                        return;

                    Categories.Clear();
                    Categories.Add("All");

                    foreach (var category in expenseCategories.Where(c => c.IsActive))
                    {
                        Categories.Add(category.Name);
                    }

                    if (!Categories.Contains("Other"))
                    {
                        Categories.Add("Other");
                    }

                    if (string.IsNullOrEmpty(SelectedCategory) && Categories.Any())
                    {
                        SelectedCategory = "All";
                    }

                    if (CurrentExpense?.Category == null && Categories.Count > 1)
                    {
                        CurrentExpense.Category = Categories[1];
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading categories: {ex.Message}");
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

        private void ApplyFilter()
        {
            if (_disposed || Expenses == null)
            {
                CategorySummaries = new ObservableCollection<CategorySummary>();
                TotalExpenseCount = 0;
                TotalExpenseAmount = 0;
                return;
            }

            bool showAll = string.IsNullOrEmpty(SelectedCategory) || SelectedCategory == "All";

            var filtered = Expenses.Where(e =>
                (showAll || e.Category == SelectedCategory) &&
                e.Date >= FilterStartDate).ToList();

            TotalExpenseCount = filtered.Count;
            TotalExpenseAmount = filtered.Sum(e => e.Amount);

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

            summaries.Add(new CategorySummary
            {
                CategoryName = "Total",
                Count = summaries.Sum(s => s.Count),
                TotalAmount = summaries.Sum(s => s.TotalAmount),
                IsTotal = true
            });

            CategorySummaries = new ObservableCollection<CategorySummary>(summaries);
        }

        private async Task SaveAsync()
        {
            if (_disposed || CurrentExpense == null)
                return;

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
            {
                return;
            }

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

                        if (Expenses != null)
                        {
                            Expenses.Insert(0, savedExpense);
                            ApplyFilter();
                        }
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
                            if (index >= 0)
                            {
                                Expenses[index] = expenseToSave;
                            }
                        }

                        CloseExpensePopup();
                        InitNewExpense();
                        ApplyFilter();
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
            if (_disposed || expense == null)
                return;

            var operationId = expense.ExpenseId;
            if (_pendingOperations.Contains(operationId))
            {
                return;
            }

            if (MessageBox.Show("Are you sure you want to delete this expense?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

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
            if (_disposed || expense == null)
                return;

            CurrentExpense = CreateExpenseClone(expense);
            IsNewExpense = false;
            ShowExpensePopup();
        }

        private void ClearForm()
        {
            if (_disposed)
                return;

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

        private async Task ShowSuccessMessage(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show(userMessage, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

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
                Categories?.Clear();
                CategorySummaries?.Clear();
            }

            base.Dispose(disposing);
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