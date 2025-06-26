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
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace QuickTechSystems.ViewModels.Transaction
{
    public class TransactionHistoryViewModel : ViewModelBase
    {
        private readonly ITransactionService _transactionService;
        private readonly IEmployeeService _employeeService;
        private readonly IServiceProvider _serviceProvider;
        private readonly SemaphoreSlim _dataLock;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private volatile bool _isDisposed = false;

        private ObservableCollection<ExtendedTransactionDTO> _allTransactions;
        private ObservableCollection<ExtendedTransactionDTO> _filteredTransactions;
        private ObservableCollection<EmployeeDTO> _employees;
        private ExtendedTransactionDTO? _selectedTransaction;
        private string? _selectedEmployeeId;
        private string? _selectedTransactionType;
        private decimal _totalSalesAmount;
        private bool _isLoading;
        private TransactionDetailsPopupViewModel? _currentPopupViewModel;

        private static readonly Dictionary<string, Func<ExtendedTransactionDTO, bool>> TransactionTypeFilters = new()
        {
            { "All Types", _ => true },
            { "Sale", t => t.TransactionType == TransactionType.Sale && !IsDebtTransaction(t) },
            { "By Dept", t => t.TransactionType == TransactionType.Sale && IsDebtTransaction(t) }
        };

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

        public ExtendedTransactionDTO? SelectedTransaction
        {
            get => _selectedTransaction;
            set => SetProperty(ref _selectedTransaction, value);
        }

        public string? SelectedEmployeeId
        {
            get => _selectedEmployeeId;
            set
            {
                if (SetProperty(ref _selectedEmployeeId, value))
                {
                    _ = ApplyFiltersAsync();
                }
            }
        }

        public string? SelectedTransactionType
        {
            get => _selectedTransactionType;
            set
            {
                if (SetProperty(ref _selectedTransactionType, value))
                {
                    _ = ApplyFiltersAsync();
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

        public bool CanExecuteCommands => !IsLoading && !_isDisposed;

        public List<string> TransactionTypes => new() { "All Types", "Sale", "By Dept" };

        public ICommand LoadDataCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand ViewTransactionDetailsCommand { get; }
        public ICommand DeleteTransactionCommand { get; }

        public TransactionHistoryViewModel(
            ITransactionService transactionService,
            IEmployeeService employeeService,
            IServiceProvider serviceProvider,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(eventAggregator, dbContextScopeService)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            _allTransactions = new ObservableCollection<ExtendedTransactionDTO>();
            _filteredTransactions = new ObservableCollection<ExtendedTransactionDTO>();
            _employees = new ObservableCollection<EmployeeDTO>();
            _dataLock = new SemaphoreSlim(1, 1);
            _cancellationTokenSource = new CancellationTokenSource();

            LoadDataCommand = new RelayCommand(async _ => await LoadDataAsync(), _ => CanExecuteCommands);
            ClearFiltersCommand = new RelayCommand(ClearFilters, _ => CanExecuteCommands);
            ViewTransactionDetailsCommand = new RelayCommand(async parameter => await OpenTransactionDetailsAsync(parameter), CanExecuteTransactionCommand);
            DeleteTransactionCommand = new RelayCommand(async parameter => await DeleteTransactionAsync(parameter), CanExecuteTransactionCommand);

            SelectedTransactionType = "All Types";
        }

        protected override async Task LoadDataImplementationAsync()
        {
            if (_isDisposed) return;

            await _dataLock.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                IsLoading = true;

                var (transactions, employees) = await Task.WhenAll(
                    LoadTransactionsAsync(),
                    LoadEmployeesAsync()
                );

                await UpdateUIAsync(() =>
                {
                    _allTransactions.Clear();
                    foreach (var transaction in transactions ?? Enumerable.Empty<ExtendedTransactionDTO>())
                    {
                        _allTransactions.Add(transaction);
                    }

                    Employees.Clear();
                    Employees.Add(new EmployeeDTO { EmployeeId = 0, FirstName = "All", LastName = "Employees" });
                    foreach (var employee in employees?.Where(e => e.IsActive) ?? Enumerable.Empty<EmployeeDTO>())
                    {
                        Employees.Add(employee);
                    }
                });

                await ApplyFiltersAsync();
                await CalculateTotalSalesAsync();
            }
            finally
            {
                IsLoading = false;
                _dataLock.Release();
            }
        }

        private async Task<IEnumerable<ExtendedTransactionDTO>?> LoadTransactionsAsync()
        {
            try
            {
                var transactions = await ExecuteDbOperationAsync(() => _transactionService.GetAllAsync(), "Loading transactions");
                return transactions?.Select(t => (ExtendedTransactionDTO)t);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading transactions: {ex}");
                return null;
            }
        }

        private async Task<IEnumerable<EmployeeDTO>?> LoadEmployeesAsync()
        {
            try
            {
                return await ExecuteDbOperationAsync(() => _employeeService.GetAllAsync(), "Loading employees");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading employees: {ex}");
                return null;
            }
        }

        private async Task ApplyFiltersAsync()
        {
            if (_isDisposed || _allTransactions == null) return;

            try
            {
                var filtered = _allTransactions.AsEnumerable();

                if (!string.IsNullOrEmpty(SelectedEmployeeId) && SelectedEmployeeId != "0")
                {
                    filtered = filtered.Where(t => t.CashierId == SelectedEmployeeId);
                }

                if (!string.IsNullOrEmpty(SelectedTransactionType) && TransactionTypeFilters.ContainsKey(SelectedTransactionType))
                {
                    filtered = filtered.Where(TransactionTypeFilters[SelectedTransactionType]);
                }

                var result = filtered.OrderByDescending(t => t.TransactionDate).ToList();

                await UpdateUIAsync(() =>
                {
                    FilteredTransactions.Clear();
                    foreach (var transaction in result)
                    {
                        FilteredTransactions.Add(transaction);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying filters: {ex}");
            }
        }

        private async Task CalculateTotalSalesAsync()
        {
            try
            {
                var totalSales = await ExecuteDbOperationAsync(() => _transactionService.GetTotalSalesAmountAsync(null, null), "Calculating total sales");
                await UpdateUIAsync(() => TotalSalesAmount = totalSales);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating total sales: {ex}");
            }
        }

        private void ClearFilters(object? parameter)
        {
            SelectedEmployeeId = null;
            SelectedTransactionType = "All Types";
        }

        private async Task OpenTransactionDetailsAsync(object? parameter)
        {
            if (parameter is not ExtendedTransactionDTO transaction || _isDisposed) return;

            try
            {
                var popupViewModel = ActivatorUtilities.CreateInstance<TransactionDetailsPopupViewModel>(_serviceProvider);
                var popupWindow = new TransactionDetailsPopup
                {
                    DataContext = popupViewModel,
                    Owner = System.Windows.Application.Current.MainWindow
                };

                popupViewModel.SetView(popupWindow);
                popupViewModel.RequestClose += (_, _) => SafeCloseWindow(popupWindow);
                popupViewModel.TransactionChanged += async (_, args) => await RefreshDataAfterChange(args.TransactionId);

                await popupViewModel.InitializeAsync(transaction);
                _currentPopupViewModel = popupViewModel;

                popupWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error opening transaction details", ex);
            }
            finally
            {
                _currentPopupViewModel?.Dispose();
                _currentPopupViewModel = null;
            }
        }

        private async Task DeleteTransactionAsync(object? parameter)
        {
            if (parameter is not ExtendedTransactionDTO transaction || _isDisposed) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete Transaction #{transaction.TransactionId}?\n\nThis will permanently remove the transaction and restock all sold items.",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsLoading = true;
                var success = await ExecuteDbOperationAsync(() => _transactionService.DeleteTransactionWithRestockAsync(transaction.TransactionId), "Deleting transaction");

                if (success)
                {
                    await ShowSuccessMessage("Transaction deleted successfully and items restocked!");
                    SelectedTransaction = null;
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
                IsLoading = false;
            }
        }

        private async Task RefreshDataAfterChange(int transactionId)
        {
            try
            {
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing data: {ex}");
            }
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator?.Subscribe<EntityChangedEvent<TransactionDTO>>(OnTransactionChanged);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator?.Unsubscribe<EntityChangedEvent<TransactionDTO>>(OnTransactionChanged);
        }

        private async void OnTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            if (_isDisposed || evt?.Entity == null) return;

            try
            {
                await UpdateUIAsync(async () =>
                {
                    var extendedTransaction = (ExtendedTransactionDTO)evt.Entity;

                    switch (evt.Action)
                    {
                        case "Create":
                            if (!_allTransactions.Any(t => t.TransactionId == evt.Entity.TransactionId))
                            {
                                _allTransactions.Insert(0, extendedTransaction);
                            }
                            break;

                        case "Update":
                            var existingIndex = _allTransactions.ToList().FindIndex(t => t.TransactionId == evt.Entity.TransactionId);
                            if (existingIndex >= 0)
                            {
                                _allTransactions[existingIndex] = extendedTransaction;
                            }
                            break;

                        case "Delete":
                            var toRemove = _allTransactions.FirstOrDefault(t => t.TransactionId == evt.Entity.TransactionId);
                            if (toRemove != null)
                            {
                                _allTransactions.Remove(toRemove);
                            }
                            break;
                    }

                    await ApplyFiltersAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling transaction changed event: {ex}");
            }
        }

        private async Task UpdateUIAsync(Action action)
        {
            if (_isDisposed) return;

            try
            {
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(action);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating UI: {ex}");
            }
        }

        private static void SafeCloseWindow(Window? window)
        {
            try
            {
                window?.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing window: {ex}");
            }
        }

        private bool CanExecuteTransactionCommand(object? parameter)
        {
            return parameter is ExtendedTransactionDTO && CanExecuteCommands;
        }

        private static bool IsDebtTransaction(ExtendedTransactionDTO transaction)
        {
            return string.Equals(transaction.PaymentMethod, "debt", StringComparison.OrdinalIgnoreCase);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                _isDisposed = true;

                try
                {
                    _cancellationTokenSource?.Cancel();
                    _currentPopupViewModel?.Dispose();
                    _dataLock?.Dispose();
                    _cancellationTokenSource?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during disposal: {ex}");
                }
            }

            base.Dispose(disposing);
        }
    }
}