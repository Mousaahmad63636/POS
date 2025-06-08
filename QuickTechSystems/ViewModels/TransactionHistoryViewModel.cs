using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Infrastructure.Data;
using System.Diagnostics;
using System.Text;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Collections.ObjectModel;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.WPF.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.Application.Helpers;

namespace QuickTechSystems.WPF.ViewModels
{
    public class TransactionHistoryViewModel : ViewModelBase
    {
        private readonly ITransactionService _transactionService;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ICategoryService _categoryService;
        private readonly IBusinessSettingsService _businessSettingsService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private TransactionDTO _selectedTransaction;
        private bool _isDisposed;
        private CancellationTokenSource _cts;
        private readonly IEmployeeService _employeeService;
        private ObservableCollection<EmployeeDTO> _employees;
        private EmployeeDTO? _selectedEmployee;

        private ObservableCollection<TransactionDTO> _transactions;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<TransactionDTO> _filteredTransactions;
        private CategoryDTO? _selectedCategory;
        private decimal _totalSales;
        private decimal _totalProfit;
        private string _searchText = string.Empty;
        private bool _isRefreshing;
        private bool _isLoading;
        private string _errorMessage = string.Empty;
        private DateTime _startDate = DateTime.Today;
        private DateTime _endDate = DateTime.Today;
        private bool _isDateRangeValid = true;
        private int _totalTransactions;

        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalPages;
        private ObservableCollection<int> _pageNumbers;
        private List<int> _visiblePageNumbers = new List<int>();

        private Dictionary<string, decimal> _categorySales = new();

        private static TransactionHistoryViewModel _instance;
        private Action<EntityChangedEvent<TransactionDTO>> _transactionChangedHandler;

        public ObservableCollection<TransactionDTO> Transactions
        {
            get => _transactions;
            private set => SetProperty(ref _transactions, value);
        }

        public ObservableCollection<TransactionDTO> FilteredTransactions
        {
            get => _filteredTransactions;
            private set => SetProperty(ref _filteredTransactions, value);
        }

        public ObservableCollection<CategoryDTO> Categories
        {
            get => _categories;
            private set => SetProperty(ref _categories, value);
        }
        public ObservableCollection<EmployeeDTO> Employees
        {
            get => _employees;
            private set => SetProperty(ref _employees, value);
        }

        public EmployeeDTO? SelectedEmployee
        {
            get => _selectedEmployee;
            set
            {
                if (SetProperty(ref _selectedEmployee, value))
                {
                    _currentPage = 1;
                    OnPropertyChanged(nameof(CurrentPage));
                    _ = SafeLoadDataAsync();
                }
            }
        }
        public CategoryDTO? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    _currentPage = 1;
                    OnPropertyChanged(nameof(CurrentPage));
                    _ = SafeLoadDataAsync();
                }
            }
        }

        public decimal TotalSales
        {
            get => _totalSales;
            private set => SetProperty(ref _totalSales, value);
        }

        public decimal TotalProfit
        {
            get => _totalProfit;
            private set => SetProperty(ref _totalProfit, value);
        }

        public int TotalTransactions
        {
            get => _totalTransactions;
            private set => SetProperty(ref _totalTransactions, value);
        }

        public Dictionary<string, decimal> CategorySales
        {
            get => _categorySales;
            private set => SetProperty(ref _categorySales, value);
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    ValidateDateRange();
                    _currentPage = 1;
                    OnPropertyChanged(nameof(CurrentPage));
                    _ = SafeLoadDataAsync();
                }
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value))
                {
                    ValidateDateRange();
                    _currentPage = 1;
                    OnPropertyChanged(nameof(CurrentPage));
                    _ = SafeLoadDataAsync();
                }
            }
        }
        public TransactionDTO SelectedTransaction
        {
            get => _selectedTransaction;
            set => SetProperty(ref _selectedTransaction, value);
        }
        public bool IsDateRangeValid
        {
            get => _isDateRangeValid;
            private set => SetProperty(ref _isDateRangeValid, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _currentPage = 1;
                    OnPropertyChanged(nameof(CurrentPage));
                    ApplyFilters();
                }
            }
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set => SetProperty(ref _isRefreshing, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (value < 1 || value > TotalPages) return;
                if (SetProperty(ref _currentPage, value))
                {
                    _ = SafeLoadDataAsync();
                    UpdateVisiblePageNumbers();
                    OnPropertyChanged(nameof(IsFirstPage));
                    OnPropertyChanged(nameof(IsLastPage));
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
                    _currentPage = 1;
                    OnPropertyChanged(nameof(CurrentPage));
                    _ = SafeLoadDataAsync();
                }
            }
        }

        public int TotalPages
        {
            get => _totalPages;
            private set
            {
                if (SetProperty(ref _totalPages, value))
                {
                    UpdateVisiblePageNumbers();
                    OnPropertyChanged(nameof(IsFirstPage));
                    OnPropertyChanged(nameof(IsLastPage));
                }
            }
        }

        public ObservableCollection<int> PageNumbers
        {
            get => _pageNumbers;
            private set => SetProperty(ref _pageNumbers, value);
        }

        public List<int> VisiblePageNumbers
        {
            get => _visiblePageNumbers;
            private set => SetProperty(ref _visiblePageNumbers, value);
        }

        public bool IsFirstPage => CurrentPage <= 1;
        public bool IsLastPage => CurrentPage >= TotalPages;

        public static void ForceRefresh()
        {
            if (_instance != null)
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _instance.RefreshCommand.Execute(null);
                });
            }
        }

        public ICommand ExportCommand { get; }
        public ICommand PrintReportCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ViewTransactionDetailsCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand DeleteTransactionCommand { get; }

        public ICommand NextPageCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand GoToPageCommand { get; }
        public ICommand ChangePageSizeCommand { get; }

        public ObservableCollection<int> AvailablePageSizes { get; } = new ObservableCollection<int> { 10, 25, 50, 100 };

        public TransactionHistoryViewModel(
     ITransactionService transactionService,
     ICategoryService categoryService,
     IBusinessSettingsService businessSettingsService,
     IDbContextFactory<ApplicationDbContext> dbContextFactory,
     IEmployeeService employeeService,
     IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _instance = this;
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
            _businessSettingsService = businessSettingsService ?? throw new ArgumentNullException(nameof(businessSettingsService));
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _transactions = new ObservableCollection<TransactionDTO>();
            _filteredTransactions = new ObservableCollection<TransactionDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _employees = new ObservableCollection<EmployeeDTO>();
            _transactionChangedHandler = HandleTransactionChanged;
            _cts = new CancellationTokenSource();
            _pageNumbers = new ObservableCollection<int>();

            ExportCommand = new AsyncRelayCommand(async _ => await ExportTransactionsAsync(), CanExecuteCommand);
            PrintReportCommand = new AsyncRelayCommand(async _ => await PrintTransactionReportAsync(), CanExecuteCommand);
            RefreshCommand = new AsyncRelayCommand(async _ => await RefreshDataAsync(), CanExecuteCommand);
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
            DeleteTransactionCommand = new AsyncRelayCommand<TransactionDTO>(
                async transaction => await DeleteTransactionAsync(transaction),
                CanDeleteTransaction);

            ViewTransactionDetailsCommand = new AsyncRelayCommand<TransactionDTO>(
    async transaction => await ShowTransactionDetailsAsync(transaction),
    CanShowTransactionDetails);
            NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => !IsLastPage);
            PreviousPageCommand = new RelayCommand(_ => CurrentPage--, _ => !IsFirstPage);
            GoToPageCommand = new RelayCommand<int>(page => CurrentPage = page);
            ChangePageSizeCommand = new RelayCommand<int>(size => PageSize = size);

            _ = InitializeAsync();
        }

        private void UpdateVisiblePageNumbers()
        {
            var visiblePages = new List<int>();
            int startPage = Math.Max(1, CurrentPage - 2);
            int endPage = Math.Min(TotalPages, CurrentPage + 2);

            if (startPage > 1)
            {
                visiblePages.Add(1);
                if (startPage > 2) visiblePages.Add(-1);
            }

            for (int i = startPage; i <= endPage; i++)
            {
                visiblePages.Add(i);
            }

            if (endPage < TotalPages)
            {
                if (endPage < TotalPages - 1) visiblePages.Add(-1);
                visiblePages.Add(TotalPages);
            }

            VisiblePageNumbers = visiblePages;
            OnPropertyChanged(nameof(VisiblePageNumbers));
        }

        private void ValidateDateRange()
        {
            IsDateRangeValid = StartDate <= EndDate;
            ErrorMessage = !IsDateRangeValid ? "Start date must be before or equal to end date" : string.Empty;
        }

        private async Task EnsureExchangeRateLoaded()
        {
            try
            {
                var rateSetting = await _businessSettingsService.GetByKeyAsync("ExchangeRate");
                if (rateSetting != null && decimal.TryParse(rateSetting.Value, out decimal rate) && rate > 0)
                {
                    CurrencyHelper.UpdateExchangeRate(rate);
                    Debug.WriteLine($"Exchange rate loaded: {rate}");
                }
                else
                {
                    CurrencyHelper.UpdateExchangeRate(100000m);
                    Debug.WriteLine("Using default exchange rate: 100000");
                }

                decimal test = CurrencyHelper.ConvertToLBP(1);
                Debug.WriteLine($"Verification: 1 USD = {test} LBP");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading exchange rate: {ex.Message}");
                CurrencyHelper.UpdateExchangeRate(100000m);
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                await LoadCategoriesAsync();
                await LoadEmployeesAsync();
                await SafeLoadDataAsync();
            }
            catch (Exception ex)
            {
                HandleError("Initialization error", ex);
            }
        }

        private async Task LoadEmployeesAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                Debug.WriteLine("LoadEmployeesAsync skipped - operation in progress");
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                try
                {
                    var employees = await _employeeService.GetAllAsync();

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Employees = new ObservableCollection<EmployeeDTO>(
                            new[] { new EmployeeDTO { EmployeeId = 0, FirstName = "All", LastName = "Employees" } }
                            .Concat(employees.Where(e => e.IsActive))
                        );
                        SelectedEmployee = Employees.First();
                    });
                }
                catch (Exception ex)
                {
                    HandleError("Error loading employees", ex);
                }
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task ShowTransactionDetailsAsync(TransactionDTO transaction)
        {
            if (transaction == null) return;

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                SelectedTransaction = transaction;

                if (transaction.Details == null || !transaction.Details.Any())
                {
                    var loadedTransaction = await _transactionService.GetByIdAsync(transaction.TransactionId);
                    if (loadedTransaction != null)
                    {
                        transaction.Details = loadedTransaction.Details;
                        SelectedTransaction = transaction;
                    }
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var popup = new QuickTechSystems.Views.TransactionDetailsPopup();
                    popup.DataContext = SelectedTransaction;

                    var overlayWindow = new Window
                    {
                        Title = $"Transaction #{transaction.TransactionId} Details",
                        Content = popup,
                        WindowState = WindowState.Maximized,
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false
                    };

                    overlayWindow.KeyDown += (s, e) =>
                    {
                        if (e.Key == Key.Escape)
                            overlayWindow.Close();
                    };

                    overlayWindow.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                HandleError("Error showing transaction details", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool CanDeleteTransaction(TransactionDTO? transaction)
        {
            return transaction != null && !IsLoading && !IsRefreshing;
        }

        public static async Task SafeRefreshAsync()
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var instance = _instance;
                    if (instance == null) return;

                    await Task.Delay(1000);

                    if (instance.RefreshCommand.CanExecute(null))
                    {
                        instance.RefreshCommand.Execute(null);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error refreshing transaction history: {ex.Message}");
                }
            });
        }

        private async Task DeleteTransactionAsync(TransactionDTO? transaction)
        {
            if (transaction == null) return;

            if (!await _operationLock.WaitAsync(0))
            {
                await ShowErrorMessageAsync("Another operation is in progress. Please try again in a moment.");
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    return MessageBox.Show(
                        GetOwnerWindow(),
                        $"Are you sure you want to delete transaction #{transaction.TransactionId}?\nThis action cannot be undone.",
                        "Confirm Deletion",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) == MessageBoxResult.Yes;
                });

                if (!result)
                {
                    return;
                }

                bool success = await _transactionService.DeleteAsync(transaction.TransactionId);

                if (success)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (Transactions.Contains(transaction))
                            Transactions.Remove(transaction);

                        if (FilteredTransactions.Contains(transaction))
                            FilteredTransactions.Remove(transaction);

                        MessageBox.Show(
                            GetOwnerWindow(),
                            $"Transaction #{transaction.TransactionId} has been deleted successfully.",
                            "Transaction Deleted",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });

                    await SafeLoadDataAsync();
                }
                else
                {
                    await ShowErrorMessageAsync($"Failed to delete transaction #{transaction.TransactionId}");
                }
            }
            catch (Exception ex)
            {
                HandleError("Error deleting transaction", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task LoadCategoriesAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                Debug.WriteLine("LoadCategoriesAsync skipped - operation in progress");
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                try
                {
                    using var context = await _dbContextFactory.CreateDbContextAsync();
                    var categories = await context.Set<Category>()
                        .Where(c => c.IsActive)
                        .Select(c => new CategoryDTO
                        {
                            CategoryId = c.CategoryId,
                            Name = c.Name,
                            Description = c.Description,
                            IsActive = c.IsActive
                        })
                        .ToListAsync();

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Categories = new ObservableCollection<CategoryDTO>(
                            new[] { new CategoryDTO { CategoryId = 0, Name = "All Categories" } }
                            .Concat(categories)
                        );
                        SelectedCategory = Categories.First();
                    });
                }
                catch (Exception ex)
                {
                    HandleError("Error loading categories", ex);
                }
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task SafeLoadDataAsync()
        {
            if (!IsDateRangeValid || !await _operationLock.WaitAsync(0))
            {
                Debug.WriteLine("SafeLoadDataAsync skipped - invalid date range or operation in progress");
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                    int? categoryId = SelectedCategory?.CategoryId > 0 ? SelectedCategory.CategoryId : null;

                    int? employeeId = SelectedEmployee?.EmployeeId > 0 ? SelectedEmployee.EmployeeId : null;

                    var (transactions, totalCount) = await _transactionService.GetByDateRangePagedAsync(
                        StartDate, EndDate, CurrentPage, PageSize, categoryId, employeeId);

                    if (linkedCts.Token.IsCancellationRequested) return;

                    var summary = await _transactionService.GetTransactionSummaryByDateRangeAsync(StartDate, EndDate);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    var categorySales = await _transactionService.GetCategorySalesByDateRangeAsync(StartDate, EndDate);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    var totalProfit = await _transactionService.GetTransactionProfitByDateRangeAsync(
                        StartDate, EndDate, categoryId, employeeId);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    int calculatedTotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!linkedCts.Token.IsCancellationRequested)
                        {
                            Transactions = new ObservableCollection<TransactionDTO>(transactions);
                            TotalTransactions = totalCount;
                            TotalPages = calculatedTotalPages;
                            TotalSales = summary;
                            TotalProfit = totalProfit;
                            CategorySales = new Dictionary<string, decimal>(categorySales);

                            ApplyFilters();
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("Operation was canceled");
                }
                catch (Exception ex)
                {
                    HandleError("Error loading transactions", ex);
                }
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private void ApplyFilters()
        {
            try
            {
                var filtered = Transactions.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    filtered = filtered.Where(t =>
                        (t.CustomerName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        t.TransactionId.ToString().Contains(SearchText) ||
                        (t.Details?.Any(d => d.ProductName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ?? false) ||
                        t.CashierName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
                }

                FilteredTransactions = new ObservableCollection<TransactionDTO>(filtered);
            }
            catch (Exception ex)
            {
                HandleError("Error applying filters", ex);
            }
        }

        private void ClearFilters()
        {
            SearchText = string.Empty;
            SelectedCategory = Categories.First();
            SelectedEmployee = Employees.First();
            StartDate = DateTime.Today;
            EndDate = DateTime.Today;
            CurrentPage = 1;
            ApplyFilters();
        }

        private void CalculateTotals()
        {
            try
            {
                var filteredSales = FilteredTransactions.Sum(t => t.TotalAmount);
                var filteredProfit = FilteredTransactions.Sum(t =>
                {
                    if (t.Details == null || !t.Details.Any())
                        return 0;

                    if (t.TotalAmount == 0)
                        return 0;

                    decimal purchaseCost = t.Details.Sum(d => d.PurchasePrice * d.Quantity);

                    return t.TotalAmount - purchaseCost;
                });
            }
            catch (Exception ex)
            {
                HandleError("Error calculating totals", ex);
            }
        }

        private async Task RefreshDataAsync()
        {
            try
            {
                IsRefreshing = true;
                await SafeLoadDataAsync();
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private bool CanExecuteCommand(object? parameter)
        {
            return !IsLoading && !IsRefreshing &&
                   FilteredTransactions.Any() &&
                   IsDateRangeValid;
        }

        private bool CanShowTransactionDetails(object? parameter)
        {
            return parameter is TransactionDTO;
        }

        private Window GetOwnerWindow()
        {
            var activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (activeWindow != null)
                return activeWindow;

            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.IsLoaded)
                return mainWindow;

            return System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                   ?? System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault();
        }

        private DateTime _lastTransactionChangedTime = DateTime.MinValue;
        private object _eventLock = new object();

        private async void HandleTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            if (IsLoading || IsRefreshing) return;

            lock (_eventLock)
            {
                var now = DateTime.Now;
                if ((now - _lastTransactionChangedTime).TotalMilliseconds < 500)
                {
                    return;
                }
                _lastTransactionChangedTime = now;
            }

            try
            {
                await Task.Delay(200);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await SafeLoadDataAsync();
                });
            }
            catch (Exception ex)
            {
                HandleError("Error handling transaction change", ex);
            }
        }

        private void HandleError(string message, Exception ex)
        {
            Debug.WriteLine($"{message}: {ex}");

            if (ex.Message.Contains("A second operation was started") ||
                (ex.InnerException != null && ex.InnerException.Message.Contains("A second operation was started")))
            {
                ErrorMessage = "The system is processing another request. Please try again in a moment.";
            }
            else if (ex.Message.Contains("The connection was closed") ||
                    (ex.InnerException != null && ex.InnerException.Message.Contains("The connection was closed")))
            {
                ErrorMessage = "Database connection lost. Please check your connection and try again.";
            }
            else
            {
                ErrorMessage = $"{message}: {ex.Message}";
            }

            Task.Run(async () =>
            {
                await Task.Delay(5000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ErrorMessage.Contains(ex.Message))
                    {
                        ErrorMessage = string.Empty;
                    }
                });
            });

            if (message.Contains("critical", StringComparison.OrdinalIgnoreCase))
            {
                var ownerWindow = GetOwnerWindow();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        ownerWindow,
                        $"{message}: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            if (!_isDisposed)
            {
                _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _operationLock.Dispose();
                    UnsubscribeFromEvents();
                }
                _isDisposed = true;
            }
        }

        private async Task ExportTransactionsAsync()
        {
            if (!FilteredTransactions.Any())
            {
                await ShowErrorMessageAsync("No transactions to export");
                return;
            }

            if (!await _operationLock.WaitAsync(0))
            {
                await ShowErrorMessageAsync("Export already in progress");
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"Transaction_History_{DateTime.Now:yyyyMMdd}"
                };

                bool? result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    saveFileDialog.ShowDialog());

                if (result == true)
                {
                    var csv = new StringBuilder();
                    csv.AppendLine("Transaction ID,Date,Customer,Type,Items,Total Amount,Profit,Status,Cashier,Category");

                    foreach (var transaction in FilteredTransactions)
                    {
                        var profit = transaction.Details?.Sum(d =>
                            (d.UnitPrice - d.PurchasePrice) * d.Quantity) ?? 0;

                        var itemCount = transaction.Details?.Count ?? 0;
                        var categories = string.Join(";", transaction.Details?
                            .Select(d => d.CategoryId)
                            .Distinct()
                            .Select(id => Categories.FirstOrDefault(c => c.CategoryId == id)?.Name ?? "Unknown")
                            ?? Array.Empty<string>());

                        csv.AppendLine($"{transaction.TransactionId}," +
                            $"\"{transaction.TransactionDate:g}\"," +
                            $"\"{transaction.CustomerName}\"," +
                            $"{transaction.TransactionType}," +
                            $"{itemCount}," +
                            $"{transaction.TotalAmount:F2}," +
                            $"{profit:F2}," +
                            $"{transaction.Status}," +
                            $"\"{transaction.CashierName}\"," +
                            $"\"{categories}\"");

                        if (transaction.Details != null)
                        {
                            foreach (var detail in transaction.Details)
                            {
                                csv.AppendLine($"," +
                                    $"," +
                                    $"," +
                                    $"Item Detail," +
                                    $"\"{detail.ProductName}\"," +
                                    $"{detail.Quantity}," +
                                    $"{detail.UnitPrice:F2}," +
                                    $"{detail.Total:F2}");
                            }
                        }
                    }

                    await File.WriteAllTextAsync(saveFileDialog.FileName, csv.ToString());
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var ownerWindow = GetOwnerWindow();
                        MessageBox.Show(
                            ownerWindow,
                            "Transactions exported successfully.",
                            "Export Complete",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                }
            }
            catch (Exception ex)
            {
                HandleError("Error exporting transactions", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task PrintTransactionReportAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                await ShowErrorMessageAsync("Print operation already in progress");
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                decimal totalSales = 0;
                int transactionCount = 0;

                try
                {
                    totalSales = await _transactionService.GetTransactionSummaryByDateRangeAsync(StartDate, EndDate);
                    transactionCount = await _transactionService.GetTransactionCountByDateRangeAsync(StartDate, EndDate);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading summary data: {ex.Message}");
                    totalSales = TotalSales;
                    transactionCount = TotalTransactions;
                }

                var printDialog = new PrintDialog();
                if (await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => printDialog.ShowDialog() == true))
                {
                    try
                    {
                        var document = CreateSimpleSummaryDocument(totalSales, transactionCount);
                        printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Transaction Summary");

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(GetOwnerWindow(), "Print completed successfully.", "Print Status", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                    catch (Exception printEx)
                    {
                        Debug.WriteLine($"Print error: {printEx.Message}");
                        await ShowErrorMessageAsync("Print failed. Please try again.");
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError("Error printing report", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private FlowDocument CreateSimpleSummaryDocument(decimal totalSales, int transactionCount)
        {
            var document = new FlowDocument
            {
                PagePadding = new Thickness(40),
                FontFamily = new FontFamily("Arial"),
                FontSize = 12
            };

            var header = new Paragraph(new Run("Sales Summary Report"))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 30)
            };
            document.Blocks.Add(header);

            var dateInfo = new Paragraph(new Run($"Period: {StartDate:MMM dd, yyyy} to {EndDate:MMM dd, yyyy}"))
            {
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };
            document.Blocks.Add(dateInfo);

            var summaryTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 20, 0, 0) };
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var rowGroup = new TableRowGroup();

            var salesRow = new TableRow();
            salesRow.Cells.Add(new TableCell(new Paragraph(new Run("Total Sales:")) { FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(5) }));
            salesRow.Cells.Add(new TableCell(new Paragraph(new Run($"${totalSales:F2} USD")) { FontSize = 14, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right, Foreground = Brushes.DarkGreen, Margin = new Thickness(5) }));
            rowGroup.Rows.Add(salesRow);

            var countRow = new TableRow();
            countRow.Cells.Add(new TableCell(new Paragraph(new Run("Total Transactions:")) { FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(5) }));
            countRow.Cells.Add(new TableCell(new Paragraph(new Run(transactionCount.ToString())) { FontSize = 14, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right, Foreground = Brushes.DarkBlue, Margin = new Thickness(5) }));
            rowGroup.Rows.Add(countRow);

            summaryTable.RowGroups.Add(rowGroup);
            document.Blocks.Add(summaryTable);

            var footer = new Paragraph(new Run($"Generated: {DateTime.Now:g}"))
            {
                FontStyle = FontStyles.Italic,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 40, 0, 0),
                FontSize = 10
            };
            document.Blocks.Add(footer);

            return document;
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            ErrorMessage = message;

            var ownerWindow = GetOwnerWindow();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    ownerWindow,
                    message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });

            await Task.Run(async () =>
            {
                await Task.Delay(3000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ErrorMessage == message)
                    {
                        ErrorMessage = string.Empty;
                    }
                });
            });
        }
    }
}