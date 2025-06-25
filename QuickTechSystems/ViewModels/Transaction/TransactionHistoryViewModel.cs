using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.Views;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace QuickTechSystems.ViewModels.Transaction
{
    public class TransactionHistoryViewModel : ViewModelBase
    {
        private readonly ITransactionService _transactionService;
        private readonly IEmployeeService _employeeService;
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, SemaphoreSlim> _operationLocks;
        private readonly Queue<Func<Task>> _operationQueue;
        private readonly SemaphoreSlim _queueProcessorLock;
        private volatile bool _isDataLoaded = false;

        private ObservableCollection<ExtendedTransactionDTO> _transactions;
        private ObservableCollection<ExtendedTransactionDTO> _filteredTransactions;
        private ObservableCollection<EmployeeDTO> _employees;
        private Dictionary<string, decimal> _employeePerformance;

        private ExtendedTransactionDTO? _selectedTransaction;
        private string _searchText = string.Empty;
        private DateTime _startDate = DateTime.Today.AddDays(-365);
        private DateTime _endDate = DateTime.Today.AddDays(1);
        private string? _selectedEmployeeId;
        private TransactionType? _selectedTransactionType;
        private TransactionStatus? _selectedTransactionStatus;
        private decimal _totalSalesAmount;
        private bool _isLoading;
        private bool _showFilters;

        private bool _isPopupVisible;
        private object? _popupContent;
        private TransactionDetailsPopupViewModel? _currentPopupViewModel;

        public ObservableCollection<ExtendedTransactionDTO> Transactions
        {
            get => _transactions;
            set => SetProperty(ref _transactions, value);
        }

        public ObservableCollection<ExtendedTransactionDTO> FilteredTransactions
        {
            get => _filteredTransactions;
            set => SetProperty(ref _filteredTransactions, value);
        }

        public ObservableCollection<EmployeeDTO> Employees
        {
            get => _employees;
            set => SetProperty(ref _employees, value);
        }

        public Dictionary<string, decimal> EmployeePerformance
        {
            get => _employeePerformance;
            set => SetProperty(ref _employeePerformance, value);
        }

        public ExtendedTransactionDTO? SelectedTransaction
        {
            get => _selectedTransaction;
            set
            {
                if (SetProperty(ref _selectedTransaction, value))
                {
                    OnPropertyChanged(nameof(IsTransactionSelected));
                    OnPropertyChanged(nameof(CanDeleteTransaction));
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _ = Task.Run(async () => await ApplyFiltersAsync());
                }
            }
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    _ = Task.Run(async () => await ApplyFiltersAsync());
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
                    _ = Task.Run(async () => await ApplyFiltersAsync());
                }
            }
        }

        public string? SelectedEmployeeId
        {
            get => _selectedEmployeeId;
            set
            {
                if (SetProperty(ref _selectedEmployeeId, value))
                {
                    _ = Task.Run(async () => await ApplyFiltersAsync());
                }
            }
        }

        public TransactionType? SelectedTransactionType
        {
            get => _selectedTransactionType;
            set
            {
                if (SetProperty(ref _selectedTransactionType, value))
                {
                    _ = Task.Run(async () => await ApplyFiltersAsync());
                }
            }
        }

        public TransactionStatus? SelectedTransactionStatus
        {
            get => _selectedTransactionStatus;
            set
            {
                if (SetProperty(ref _selectedTransactionStatus, value))
                {
                    _ = Task.Run(async () => await ApplyFiltersAsync());
                }
            }
        }

        public decimal TotalSalesAmount
        {
            get => _totalSalesAmount;
            set => SetProperty(ref _totalSalesAmount, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool ShowFilters
        {
            get => _showFilters;
            set => SetProperty(ref _showFilters, value);
        }

        public bool IsPopupVisible
        {
            get => _isPopupVisible;
            set => SetProperty(ref _isPopupVisible, value);
        }

        public object? PopupContent
        {
            get => _popupContent;
            set => SetProperty(ref _popupContent, value);
        }

        public bool IsTransactionSelected => SelectedTransaction != null;
        public bool CanDeleteTransaction => IsTransactionSelected;

        public Array TransactionTypes => Enum.GetValues(typeof(TransactionType));
        public Array TransactionStatuses => Enum.GetValues(typeof(TransactionStatus));

        public ICommand LoadDataCommand { get; }
        public ICommand ApplyFiltersCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand ToggleFiltersCommand { get; }
        public ICommand ViewTransactionDetailsCommand { get; }
        public ICommand DeleteTransactionCommand { get; }
        public ICommand RefreshEmployeePerformanceCommand { get; }
        public ICommand ExportTransactionsCommand { get; }

        public TransactionHistoryViewModel(
            ITransactionService transactionService,
            IEmployeeService employeeService,
            IServiceProvider serviceProvider,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(eventAggregator, dbContextScopeService)
        {
            _transactionService = transactionService;
            _employeeService = employeeService;
            _serviceProvider = serviceProvider;

            _transactions = new ObservableCollection<ExtendedTransactionDTO>();
            _filteredTransactions = new ObservableCollection<ExtendedTransactionDTO>();
            _employees = new ObservableCollection<EmployeeDTO>();
            _employeePerformance = new Dictionary<string, decimal>();
            _operationLocks = new Dictionary<string, SemaphoreSlim>();
            _operationQueue = new Queue<Func<Task>>();
            _queueProcessorLock = new SemaphoreSlim(1, 1);

            LoadDataCommand = new RelayCommand(async _ => await LoadDataAsync());
            ApplyFiltersCommand = new RelayCommand(async _ => await ApplyFiltersAsync());
            ClearFiltersCommand = new RelayCommand(async _ => await ClearFiltersAsync());
            ToggleFiltersCommand = new RelayCommand(_ => ShowFilters = !ShowFilters);
            ViewTransactionDetailsCommand = new RelayCommand(async parameter => await OpenTransactionDetailsPopupAsync(parameter));
            DeleteTransactionCommand = new RelayCommand(async parameter => await DeleteTransactionAsync(parameter));
            RefreshEmployeePerformanceCommand = new RelayCommand(async _ => await LoadEmployeePerformanceAsync());
            ExportTransactionsCommand = new RelayCommand(async _ => await ExportTransactionsAsync());

            InitializeOperationLocks();
        }

        private void InitializeOperationLocks()
        {
            var lockNames = new[] { "LoadData", "ApplyFilters", "DeleteTransaction", "PopupManagement" };
            foreach (var lockName in lockNames)
            {
                _operationLocks[lockName] = new SemaphoreSlim(1, 1);
            }
        }

        protected override async Task LoadDataImplementationAsync()
        {
            await _operationLocks["LoadData"].WaitAsync();
            try
            {
                IsLoading = true;
                Debug.WriteLine("Starting to load transaction history data...");

                await LoadTransactionsDirectlyAsync();
                await LoadEmployeesAsync();
                await LoadEmployeePerformanceAsync();
                await CalculateTotalSalesAsync();

                _isDataLoaded = true;
                Debug.WriteLine($"Loaded {Transactions.Count} transactions successfully");

                await ApplyFiltersAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadDataImplementationAsync: {ex}");
                await HandleExceptionAsync("Error loading transaction history data", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLocks["LoadData"].Release();
            }
        }

        private async Task LoadTransactionsDirectlyAsync()
        {
            try
            {
                Debug.WriteLine("Loading transactions from database...");

                var transactions = await ExecuteDbOperationAsync(
                    () => _transactionService.GetAllAsync(),
                    "Loading transactions");

                Debug.WriteLine($"Retrieved {transactions.Count()} transactions from service");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Transactions.Clear();
                    foreach (var transaction in transactions)
                    {
                        try
                        {
                            var extendedTransaction = (ExtendedTransactionDTO)transaction;
                            Transactions.Add(extendedTransaction);
                            Debug.WriteLine($"Added transaction {transaction.TransactionId}: {transaction.CustomerName}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error converting transaction {transaction.TransactionId}: {ex.Message}");
                        }
                    }
                    Debug.WriteLine($"Total transactions in collection: {Transactions.Count}");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadTransactionsDirectlyAsync: {ex}");
                throw;
            }
        }

        private async Task LoadEmployeesAsync()
        {
            try
            {
                var employees = await ExecuteDbOperationAsync(
                    () => _employeeService.GetAllAsync(),
                    "Loading employees");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Employees.Clear();
                    Employees.Add(new EmployeeDTO { EmployeeId = 0, FirstName = "All", LastName = "Employees" });
                    foreach (var employee in employees.Where(e => e.IsActive))
                    {
                        Employees.Add(employee);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading employees: {ex}");
            }
        }

        private async Task LoadEmployeePerformanceAsync()
        {
            try
            {
                var performance = await ExecuteDbOperationAsync(
                    () => _transactionService.GetEmployeePerformanceAsync(StartDate, EndDate),
                    "Loading employee performance");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    EmployeePerformance = performance;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading employee performance: {ex}");
            }
        }

        private async Task CalculateTotalSalesAsync()
        {
            try
            {
                var totalSales = await ExecuteDbOperationAsync(
                    () => _transactionService.GetTotalSalesAmountAsync(StartDate, EndDate),
                    "Calculating total sales");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TotalSalesAmount = totalSales;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating total sales: {ex}");
            }
        }

        private async Task ApplyFiltersAsync()
        {
            if (!_isDataLoaded)
            {
                Debug.WriteLine("Skipping ApplyFilters - data not loaded yet");
                return;
            }

            await _operationLocks["ApplyFilters"].WaitAsync();
            try
            {
                Debug.WriteLine($"Applying filters - Total transactions: {Transactions.Count}");
                Debug.WriteLine($"Date range: {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}");

                var filteredList = new List<ExtendedTransactionDTO>();

                foreach (var transaction in Transactions)
                {
                    bool includeTransaction = true;

                    if (transaction.TransactionDate.Date < StartDate.Date || transaction.TransactionDate.Date > EndDate.Date)
                    {
                        includeTransaction = false;
                    }

                    if (includeTransaction && !string.IsNullOrEmpty(SelectedEmployeeId) && SelectedEmployeeId != "0")
                    {
                        if (transaction.CashierId != SelectedEmployeeId)
                        {
                            includeTransaction = false;
                        }
                    }

                    if (includeTransaction && SelectedTransactionType.HasValue)
                    {
                        if (transaction.TransactionType != SelectedTransactionType.Value)
                        {
                            includeTransaction = false;
                        }
                    }

                    if (includeTransaction && SelectedTransactionStatus.HasValue)
                    {
                        if (transaction.Status != SelectedTransactionStatus.Value)
                        {
                            includeTransaction = false;
                        }
                    }

                    if (includeTransaction && !string.IsNullOrWhiteSpace(SearchText))
                    {
                        var searchLower = SearchText.ToLower();
                        if (!transaction.CustomerName.ToLower().Contains(searchLower) &&
                            !transaction.CashierName.ToLower().Contains(searchLower) &&
                            !transaction.PaymentMethod.ToLower().Contains(searchLower) &&
                            !transaction.TransactionId.ToString().Contains(SearchText))
                        {
                            includeTransaction = false;
                        }
                    }

                    if (includeTransaction)
                    {
                        filteredList.Add(transaction);
                    }
                }

                Debug.WriteLine($"Filtered to {filteredList.Count} transactions");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FilteredTransactions.Clear();
                    foreach (var transaction in filteredList.OrderByDescending(t => t.TransactionDate))
                    {
                        FilteredTransactions.Add(transaction);
                    }
                });

                await CalculateTotalSalesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ApplyFiltersAsync: {ex}");
                await HandleExceptionAsync("Error applying filters", ex);
            }
            finally
            {
                _operationLocks["ApplyFilters"].Release();
            }
        }

        private async Task ClearFiltersAsync()
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SearchText = string.Empty;
                StartDate = DateTime.Today.AddDays(-365);
                EndDate = DateTime.Today.AddDays(1);
                SelectedEmployeeId = null;
                SelectedTransactionType = null;
                SelectedTransactionStatus = null;
            });

            await ApplyFiltersAsync();
        }

        private async Task OpenTransactionDetailsPopupAsync(object? parameter)
        {
            if (parameter is not ExtendedTransactionDTO transaction) return;

            await _operationLocks["PopupManagement"].WaitAsync();
            try
            {
                // Always create new instances to avoid disposed object issues
                var popupViewModel = ActivatorUtilities.CreateInstance<TransactionDetailsPopupViewModel>(_serviceProvider);
                var popupWindow = new TransactionDetailsPopup();

                popupWindow.DataContext = popupViewModel;
                popupWindow.Owner = System.Windows.Application.Current.MainWindow;

                popupViewModel.SetView(popupWindow);

                // Setup event handlers
                popupViewModel.RequestClose += (sender, args) => popupWindow.Close();
                popupViewModel.TransactionChanged += async (sender, args) => await RefreshTransactionDataAsync(args.TransactionId);

                // Initialize the popup
                await popupViewModel.InitializeAsync(transaction);

                _currentPopupViewModel = popupViewModel;

                // Show dialog and clean up after it closes
                popupWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error opening transaction details", ex);
            }
            finally
            {
                // Clean up after dialog closes
                _currentPopupViewModel?.Dispose();
                _currentPopupViewModel = null;
                _operationLocks["PopupManagement"].Release();
            }
        }
        private void ClosePopup()
        {
            IsPopupVisible = false;
            PopupContent = null;
            _currentPopupViewModel?.Dispose();
            _currentPopupViewModel = null;
        }

        private async Task RefreshTransactionDataAsync(int transactionId)
        {
            try
            {
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing transaction data: {ex}");
            }
        }

        private async Task DeleteTransactionAsync(object? parameter)
        {
            if (parameter is not ExtendedTransactionDTO transaction) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete Transaction #{transaction.TransactionId}?\n\n" +
                "This will permanently remove the transaction and restock all sold items.",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            await _operationLocks["DeleteTransaction"].WaitAsync();
            try
            {
                var success = await ExecuteDbOperationAsync(
                    () => _transactionService.DeleteTransactionWithRestockAsync(transaction.TransactionId),
                    "Deleting transaction");

                if (success)
                {
                    await ShowSuccessMessage("Transaction deleted successfully and items restocked!");
                    SelectedTransaction = null;
                    await Task.Delay(100);
                    await LoadDataAsync();
                }
                else
                {
                    ShowTemporaryErrorMessage("Failed to delete transaction.");
                }
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error deleting transaction", ex);
            }
            finally
            {
                _operationLocks["DeleteTransaction"].Release();
            }
        }

        private async Task ExportTransactionsAsync()
        {
            ShowTemporaryErrorMessage("Export functionality coming soon!");
            await Task.CompletedTask;
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(OnTransactionChanged);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(OnTransactionChanged);
        }

        private async void OnTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                switch (evt.Action)
                {
                    case "Create":
                        if (!Transactions.Any(t => t.TransactionId == evt.Entity.TransactionId))
                        {
                            var extendedTransaction = (ExtendedTransactionDTO)evt.Entity;
                            Transactions.Insert(0, extendedTransaction);
                        }
                        break;

                    case "Update":
                        var existingTransaction = Transactions.FirstOrDefault(t => t.TransactionId == evt.Entity.TransactionId);
                        if (existingTransaction != null)
                        {
                            var index = Transactions.IndexOf(existingTransaction);
                            var extendedTransaction = (ExtendedTransactionDTO)evt.Entity;
                            Transactions[index] = extendedTransaction;
                        }
                        break;

                    case "Delete":
                        var transactionToRemove = Transactions.FirstOrDefault(t => t.TransactionId == evt.Entity.TransactionId);
                        if (transactionToRemove != null)
                        {
                            Transactions.Remove(transactionToRemove);
                        }
                        break;
                }

                await ApplyFiltersAsync();
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClosePopup();

                foreach (var lockPair in _operationLocks)
                {
                    lockPair.Value?.Dispose();
                }
                _operationLocks.Clear();
                _queueProcessorLock?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}